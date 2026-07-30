using System.Runtime.CompilerServices;
using CSLua.Util;

// ReSharper disable InconsistentNaming

namespace CSLua;

public sealed class LuaTable
{
	public LuaTable? MetaTable;

	internal uint NoTagMethodFlags;

	private readonly LuaState L;
	private TValue[] _arrayPart;
	private HNode[] _hashPart = null!; // InitLuaTable();
	private int _lastFree;
	private int _arraySize;
	private int _hashSize;

	public LuaTable(LuaState l)
	{
		L = l;
		
		_arrayPart = [];
		_arraySize = _arrayPart.Length;
		SetNodeVector(0);
	}

	public TValue? TryGet(TValue key) => 
		TryGet(key, out var result) ? result : null;

	public bool TryGet(TValue key, out StkId value)
	{
		value = StkId.Nil;
		return key.Type != Lua.Type.LUA_TNIL && (IsPositiveInteger(key)
			? TryGet((int)key.NValue, out value)
			: TryGetByHash(key, ComputeHash(key), out value));
	}
	
	public bool TryGet(int key, out StkId value)
	{
		value = StkId.Nil;
		if (0 < key && key - 1 < _arraySize)
		{
			value = ArrayRef(key - 1);
			return true;
		}

		for (var i = GetHashNodeIndex(key); i >= 0; i = _hashPart[i].NextIndex)
		{
			// ReSharper disable once CompareOfFloatsByEqualityOperator
			if (!_hashPart[i].Key.IsNumber() || _hashPart[i].Key.NValue != key)
				continue;
			value = HashValRef(i);
			return true;
		}

		return false;
	}
	
	private bool TryGetByHash(TValue key, int hash, out StkId value)
	{
		value = StkId.Nil;
		for (var i = GetHashNodeIndex(hash); i >= 0; i = _hashPart[i].NextIndex)
		{
			if (_hashPart[i].Key != key) continue;
			value = HashValRef(i);
			return true;
		}
		return false;
	}

	public void Set(TValue key, TValue val)
	{
		if (key.Type == Lua.Type.LUA_TNIL)
			L.RunError("Table index is nil");
		
		var hash = ComputeHash(key);
		var found = IsPositiveInteger(key) 
			? TryGet((int)key.NValue, out var value) 
			: TryGetByHash(key, hash, out value);

		if (!found) 
			value = NewTableKey(key, hash);
			
		value.Set(val);
	}
	
	public void Set(int key, TValue val)
	{
		if (!TryGet(key, out var value))
		{
			NewTableKey(key, key).Set(val);
			return;
		}
		value.Set(val);
	}
	
	public void Set(TValue key, Lua.CsDelegate fun) =>
		Set(key, TValue.Of(fun));

	public bool Next(StkId key, StkId val)
	{
		// Find original element
		var i = FindIndex(key);

		// Try first array part
		for (i++; i < _arraySize; ++i)
		{
			if (_arrayPart[i].IsNil()) continue;

			key.V.SetDouble(i + 1);
			val.Set(_arrayPart[i]);
			return true;
		}

		// Then hash part
		for (i -= _arraySize; i < _hashSize; ++i)
		{
			if (_hashPart[i].Val.IsNil()) continue;

			key.Set(_hashPart[i].Key);
			val.Set(_hashPart[i].Val);
			return true;
		}

		// No more elements
		return false;
	}

	public int Length
	{ 
		get 
		{
			var j = (uint)_arraySize;
			if (j > 0 && _arrayPart[j - 1].IsNil()) 
			{
				// There is a boundary in the array part: (binary) search for it
				uint i = 0;
				while (j - i > 1) 
				{
					var m = (i + j) / 2;
					if (_arrayPart[m - 1].IsNil()) j = m;
					else i = m;
				}
				return (int)i;
			}

			// Else must find a boundary in hash part
			return _hashPart == DummyHashPart ? _arraySize : UnboundSearch(j);
		}
	}

	public void Resize(int naSize, int nhSize)
	{
		var oaSize = _arraySize;
		var oldHashPart = _hashPart;
		var oldHashPartSize = _hashSize;
		
		if (naSize > oaSize) // Array part must grow?
			SetArrayVector(naSize);

		// Create new hash part with appropriate size
		SetNodeVector(nhSize);

		// Array part must shrink?
		if (naSize < oaSize)
		{
			var oldArrayPart = _arrayPart;
			_arrayPart = oldArrayPart;
			_arraySize = naSize;
			
			// Re-insert elements from vanishing slice
			for (var i = naSize; i < oaSize; ++i) 
			{
				if (!oldArrayPart[i].IsNil()) 
					Set(i + 1, oldArrayPart[i]);
			}
			
			// Shrink array
			for (var i = naSize; i < oaSize; ++i) 
				oldArrayPart[i].SetNil();
		}

		// Re-insert elements from hash part
		for (var i = oldHashPartSize - 1; i >= 0; i--)
		{
			if (!oldHashPart[i].Val.IsNil())
				Set(oldHashPart[i].Key, oldHashPart[i].Val);
		}
	}

	private bool Get(TValue key, out StkId value)
	{
		value = StkId.Nil;
		if (key.Type == Lua.Type.LUA_TNIL)
			return false;
		return IsPositiveInteger(key) 
			? TryGet((int)key.NValue, out value) 
			: TryGetByHash(key, ComputeHash(key), out value);
	}

	/// <summary>
	/// Returns the index of a 'key' for table traversals. First goes all
	/// elements in the array part, then elements in the hash part. The
	/// beginning of a traversal is signaled by -1.
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	private int FindIndex(StkId key)
	{
		if (key.V.IsNil()) return -1;

		// Is 'key' inside array part?
		if (ArrayIndex(key.V) is {} i and > 0 && i <= _arraySize)
			return i - 1;

		var hash = ComputeHash(key.V);
		var n = GetHashNodeIndex(hash);
		
		while (n >= 0)
		{
			if (L.RawEqualObj(HashKeyRef(n), key))
				return _arraySize + n;
			n = _hashPart[n].NextIndex;
		}
		
		// key not found
		L.RunError("Invalid key to 'next'");
		return -1;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private StkId ArrayRef(int index) => new (ref _arrayPart[index]);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private StkId HashValRef(int index) => new (ref _hashPart[index].Val);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private StkId HashKeyRef(int index) => new (ref _hashPart[index].Key);

	private struct HNode()
	{
		public TValue Key = TValue.Nil();
		public TValue Val = TValue.Nil();
		public int NextIndex = -1;
	}

	private static readonly HNode[] DummyHashPart = [new()];

	private const int MAXBITS = 30;
	private const int MAXASIZE = 1 << MAXBITS;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int ComputeHash(TValue key)
	{
		if (IsPositiveInteger(key)) return (int)key.NValue;
		if (key.AsString() is {} str) return str.GetHashCode();
		return key.GetHashCode();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetHashNodeIndex(int hashcode) => 
		(int)(((uint)hashcode) & (_hashSize - 1));

	private void SetArrayVector(int size)
	{
		LuaUtil.Assert(size >= _arraySize);

		if (size > _arrayPart.Length) 
			Array.Resize(ref _arrayPart, size);

		var i = _arraySize;
		for (; i < size; ++i) _arrayPart[i] = TValue.Nil();

		_arraySize = size;
	}

	private void SetNodeVector(int size)
	{
		if (size == 0) 
		{
			_hashPart = DummyHashPart;
			_hashSize = _hashPart.Length;
			_lastFree = size;
			return;
		}

		var lsize = CeilLog2(size);
		if (lsize > MAXBITS) L.RunError("Table overflow");

		size = (1 << lsize);

		_hashPart = new HNode[size];
		_hashPart.AsSpan().Fill(new HNode());

		_hashSize = size;
		_lastFree = size;
	}

	private int? GetFreePos()
	{
		while (_lastFree > 0) 
		{
			var i = --_lastFree;
			if (_hashPart[i].Key.IsNil()) return i;
		}
		return null;
	}

	/*
	 ** Returns the index for 'key' if 'key' is an appropriate key to live in
	 ** the array part of the table, -1 otherwise.
	 */
	private static int? ArrayIndex(TValue k) =>
		IsPositiveInteger(k) ? (int)k.NValue : null;

	private static readonly byte[] Log2 =
	[
		0,1,2,2,3,3,3,3,4,4,4,4,4,4,4,4,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
		6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
		7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
		7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
	];

	private static int CeilLog2(int x)
	{
		LuaUtil.Assert(x > 0);
		var l = 0;
		x--;
		while (x >= 256) { l += 8; x >>= 8; }
		return l + Log2[x];
	}

	private static int CountInt(TValue key, Span<int> nums)
	{
		if (ArrayIndex(key) is not ({} k and > 0 and <= MAXASIZE))
			return 0;
		nums[CeilLog2(k)]++;
		return 1;
	}

	private int NumUseArray(Span<int> nums)
	{
		var ause = 0;
		var i = 1;
		for (int lg = 0, ttlg = 1; lg <= MAXBITS; lg++, ttlg *= 2) 
		{
			var lc = 0; // counter
			var lim = ttlg;
			if (lim > _arraySize) 
			{
				lim = _arraySize;
				if (i > lim) break; // No more elements to count
			}

			// Count elements in range (2^(lg-1), 2^lg]
			for (; i <= lim; ++i)
				if (!_arrayPart[i - 1].IsNil()) lc++;
			nums[lg] += lc;
			ause += lc;
		}
		return ause;
	}

	private int NumUseHash(Span<int> nums, ref int naSize)
	{
		var totalUse = 0;
		var ause = 0;
		var i = _hashSize;
		while (i-- > 0)
		{
			if (_hashPart[i].Val.IsNil()) continue;
			ause += CountInt(_hashPart[i].Key, nums);
			totalUse++;
		}
		naSize += ause;
		return totalUse;
	}

	private static int ComputeSizes(ReadOnlySpan<int> nums, ref int naSize)
	{
		var a = 0;
		var na = 0;
		var n = 0;
		for (int i = 0, tti = 1; tti / 2 < naSize; ++i, tti *= 2) 
		{
			if (nums[i] > 0) 
			{
				a += nums[i];
				if (a > tti / 2) 
				{
					n = tti;
					na = a;
				}
			}
			if (a == naSize) break; // All elements already counted
		}
		naSize = n;
		LuaUtil.Assert(naSize / 2 <= na && na <= naSize);
		return na;
	}

	private void Rehash(TValue k)
	{
		Span<int> nums = stackalloc int[MAXBITS + 1];
		var naSize = NumUseArray(nums);
		var totalUse = naSize;
		totalUse += NumUseHash(nums, ref naSize);
		naSize += CountInt(k, nums);
		totalUse++;
		var na = ComputeSizes(nums, ref naSize);
		Resize(naSize, totalUse - na);
	}

	private StkId NewTableKey(TValue k, int hash)
	{
		if (k.IsNumber() && double.IsNaN(k.NValue))
			L.RunError("Table index is NaN");
		
		while (true)
		{
			var mainPos = GetHashNodeIndex(hash);

			// If main position is taken
			if (_hashPart == DummyHashPart || !_hashPart[mainPos].Val.IsNil())
			{
				if (GetFreePos() is not {} freePos)
				{
					Rehash(k);
					if (!Get(k, out var cell)) continue;
					return cell;
				}

				var otherN = GetHashNodeIndex(ComputeHash(_hashPart[mainPos].Key));
				
				// Is colliding node out of its main position?
				if (otherN != mainPos)
				{
					while (_hashPart[otherN].NextIndex != mainPos) 
						otherN = _hashPart[otherN].NextIndex;
						
					_hashPart[otherN].NextIndex = freePos;
					_hashPart[freePos] = _hashPart[mainPos];
					
					_hashPart[mainPos].NextIndex = -1;
					_hashPart[mainPos].Val.SetNil();
				}
				// Colliding node is in its own main position
				else
				{
					_hashPart[freePos].NextIndex = _hashPart[mainPos].NextIndex;
					_hashPart[mainPos].NextIndex = freePos;
					mainPos = freePos;
				}
			}

			_hashPart[mainPos].Key = k;
			LuaUtil.Assert(_hashPart[mainPos].Val.IsNil());
			return HashValRef(mainPos);
		}
	}

	private int UnboundSearch(uint j)
	{
		var i = j;
		j++;
		while (TryGet((int)j, out var v) && !v.V.IsNil()) 
		{
			i = j;
			j *= 2;

			// Overflow?
			if (j <= LuaLimits.MAX_INT) continue;

			// Table was built with bad purposes: resort to linear search
			i = 1;
			while (TryGet((int)i, out var v2) && !v2.V.IsNil()) i++;
			return (int)(i - 1);
		}
		// Now do a binary search between them
		while (j - i > 1) 
		{
			var m = (i + j) / 2;
			if (!TryGet((int)m, out var v) || v.V.IsNil()) j = m;
			else i = m;
		}
		return (int)i;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsPositiveInteger(TValue v)
	{
		if (!v.IsNumber() || v.NValue <= 0 || v.NValue > int.MaxValue)
			return false;
		var intVal = (int)v.NValue; 
		// ReSharper disable once CompareOfFloatsByEqualityOperator
		return intVal == v.NValue;
		// Fix large number key bug
	}
}