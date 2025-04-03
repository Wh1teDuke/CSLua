using CSLua.Util;

// ReSharper disable InconsistentNaming

namespace CSLua;

public sealed class LuaTable
{
	public LuaTable? MetaTable;
	public uint NoTagMethodFlags;

	private readonly LuaState L;
	private TValue[] _arrayPart = null!;
	private HNode[] _hashPart = null!;
	private int _lastFree;
	private int _arraySize;
	private int _hashSize;

	public LuaTable(LuaState l)
	{
		L = l;
		InitLuaTable();
	}

	public bool TryGet(StkId key, out StkId value)
	{
		value = StkId.Nil;
		
		if (key.V.Type == (int)LuaType.LUA_TNIL) 
			return false;
		if (IsPositiveInteger(key))
			return TryGetInt((int)key.V.NValue, out value);
		if (key.V.Type == LuaType.LUA_TSTRING)
			return TryGetStr(key.V.AsString()!, out value);

		var h = key.V.GetHashCode();
		for (var node = GetHashNode(h); node != null; node = node.Next) 
		{
			if (node.Key == key.V)
			{
				value = node.PtrVal;
				return true;
			}
		}

		return false;
	}

	public TValue? TryGet(string key)
	{
		var val = new TValue();
		val.SetString(key);
		if (TryGet(new StkId(ref val), out var result))
			return result.V;
		return null;
	}
	
	public bool TryGetStr(string key, out StkId value)
	{
		value = StkId.Nil;
		var h = key.GetHashCode();
		for (var node = GetHashNode(h); node != null; node = node.Next) 
		{
			if (node.Key.IsString() && node.Key.AsString() == key)
			{
				value = node.PtrVal;
				return true;
			}
		}

		return false;
	}
	
	public bool TryGetInt(int key, out StkId value)
	{
		value = StkId.Nil;
		if (0 < key && key - 1 < _arraySize)
		{
			value = new StkId(ref _arrayPart[key - 1]);
			return true;
		}

		var k = new TValue();
		k.SetDouble(key);
		for (var node = GetHashNode(new StkId(ref k)); node != null; node = node.Next) 
		{
			if (node.Key.IsNumber() && node.Key.NValue == key)
			{
				value = node.PtrVal;
				return true;
			}
		}

		return false;
	}

	private bool Get(StkId key, out StkId value)
	{
		value = StkId.Nil;
		if (key.V.Type == (int)LuaType.LUA_TNIL)
			return false;
		if (IsPositiveInteger(key))
			return TryGetInt((int)key.V.NValue, out value);
		if (key.V.Type == LuaType.LUA_TSTRING)
			return TryGetStr(key.V.AsString()!, out value);

		var h = key.V.GetHashCode();
		for (var node = GetHashNode(h); node != null; node = node.Next) 
		{
			if (node.Key == key.V)
			{
				value = node.PtrVal;
				return true;
			}
		}

		return false;
	}

	public void Set(StkId key, StkId val)
	{
		if (!TryGet(key, out var value)) 
			value = NewTableKey(key);
		value.Set(val);
	}

	public void Set(string key, string val)
	{
		var k = new TValue();
		var v = new TValue();
		
		k.SetString(key);
		v.SetString(val);
		
		Set(new StkId(ref k), new StkId(ref v));
	}
	
	public void Set(string key, object val)
	{
		var k = new TValue();
		var v = new TValue();
		
		k.SetString(key);
		v.SetUserData(val);
		
		Set(new StkId(ref k), new StkId(ref v));
	}
	
	public void Set(string key, int val)
	{
		var k = new TValue();
		var v = new TValue();
		
		k.SetString(key);
		v.SetDouble(val);
		
		Set(new StkId(ref k), new StkId(ref v));
	}

	public void Set(string key, CsDelegate val) =>
		Set(key, new CsClosure(val));

	public void Set(string key, CsClosure val)
	{
		var k = new TValue();
		var v = new TValue();
		
		k.SetString(key);
		v.SetCSClosure(val);

		Set(new StkId(ref k), new StkId(ref v));
	}

	public void SetInt(int key, StkId val)
	{
		if (!TryGetInt(key, out var value))
		{
			var k = new TValue();
			k.SetDouble(key);
			var value2 = NewTableKey(new StkId(ref k));
			value2.Set(val);
			return;
		}
		value.Set(val);
	}

	/*
	 ** Returns the index of a 'key' for table traversals. First goes all
	 ** elements in the array part, then elements in the hash part. The
	 ** beginning of a traversal is signaled by -1.
	 */
	private int FindIndex(StkId key)
	{
		if (key.V.IsNil()) return -1;

		// Is 'key' inside array part?
		if (ArrayIndex(key) is {} i and > 0 && i <= _arraySize)
			return i - 1;

		var n = GetHashNode(key);
		// Check whether 'key' is somewhere in the chain
		for (;;) 
		{
			if (L.RawEqualObj(n!.PtrKey, key))
				return _arraySize + n.Index;
			n = n.Next;

			// key not found
			if (n == null) L.RunError("Invalid key to 'next'");
		}
	}

	public bool Next(StkId key, StkId val)
	{
		// Find original element
		var i = FindIndex(key);

		// Try first array part
		for (i++; i < _arraySize; ++i)
		{
			if (_arrayPart[i].IsNil()) continue;

			key.V.SetDouble(i + 1);
			val.Set(new StkId(ref _arrayPart[i]));
			return true;
		}

		// Then hash part
		for (i -= _arraySize; i < _hashSize; ++i)
		{
			if (_hashPart[i].Val.IsNil()) continue;

			key.Set(_hashPart[i].PtrKey);
			val.Set(_hashPart[i].PtrVal);
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
			if (_hashPart == Dummy.HashPart) return (int)j;
			return UnboundSearch(j);
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
			for (var i= naSize; i < oaSize; ++i) 
			{
				if (!oldArrayPart[i].IsNil()) 
					SetInt(i + 1, new StkId(ref oldArrayPart[i]));
			}
			
			// Shrink array
			for (var i = naSize; i < oaSize; ++i)
			{
				oldArrayPart[i].SetNil();
			}
		}

		// Re-insert elements from hash part
		for (var i = oldHashPartSize - 1; i >= 0; i--)
		{
			var node = oldHashPart[i];
			if (!node.Val.IsNil())
				Set(node.PtrKey, node.PtrVal);
		}
	}

	//-----------------------------------------
	//
	// **** PRIVATE below ****
	//
	//-----------------------------------------

	private sealed class HNode
	{
		public TValue Key;
		public TValue Val;
		public HNode? Next;
		public int Index;

		public StkId PtrKey => new (ref Key);
		public StkId PtrVal => new (ref Val);

		public void CopyFrom(HNode o)
		{
			Key.SetObj(o.PtrKey);
			Val.SetObj(o.PtrVal);
			Next = o.Next;
		}
	}

	private readonly struct DummyData
	{
		public readonly HNode Node;
		public readonly HNode[] HashPart;

		public DummyData()
		{
			var nil = TValue.Nil();
			Node = new HNode { Key = nil, Val = nil, Next = null, Index = 0, };
			HashPart = [Node];
		}
	}

	private static readonly DummyData Dummy = new();

	private const int MAXBITS = 30;
	private const int MAXASIZE = 1 << MAXBITS;

	private static HNode NewHNode()
	{
		var newNode = new HNode();
		newNode.Key.SetNil();
		newNode.Val.SetNil();
		return newNode;
	}

	private void InitLuaTable()
	{
		_arrayPart = [];
		_arraySize = _arrayPart.Length;
		SetNodeVector(0);
	}

	private static bool IsPositiveInteger(StkId v) =>
		v.V.IsNumber() && v.V.NValue > 0 &&
		v.V.NValue % 1 == 0 &&
		v.V.NValue <= int.MaxValue; // Fix large number key bug

	private HNode GetHashNode(int hashcode)
	{
		var n = (uint)hashcode;
		return _hashPart[n % _hashSize];
	}

	// hashnum
	private HNode GetHashNode(StkId v)
	{
		if (IsPositiveInteger(v)) return GetHashNode((int)v.V.NValue);
		if (v.V.IsString()) return GetHashNode(v.V.AsString()!.GetHashCode());
		return GetHashNode(v.V.GetHashCode());
	}

	private void SetArrayVector(int size)
	{
		LuaUtil.Assert(size >= _arraySize);

		if (size > _arrayPart.Length) 
			Array.Resize(ref _arrayPart, size);

		var i = _arraySize;
		for (; i < size; ++i) 
			_arrayPart[i].SetNil();

		_arraySize = size;
	}

	private void SetNodeVector(int size)
	{
		if (size == 0) 
		{
			_hashPart = Dummy.HashPart;
			_hashSize = _hashPart.Length;
			_lastFree = size;
			return;
		}

		var lsize = CeilLog2(size);
		if (lsize > MAXBITS) L.RunError("Table overflow");

		size = (1 << lsize);

		_hashPart = new HNode[size];
		for (var i = 0; i < size; ++i)
		{
			_hashPart[i] = NewHNode();
			_hashPart[i].Index = i;
		}

		_hashSize = size;
		_lastFree = size;
	}

	private HNode? GetFreePos()
	{
		while (_lastFree > 0) 
		{
			var node = _hashPart[--_lastFree];
			if (node.Key.IsNil()) return node;
		}
		return null;
	}

	/*
	 ** Returns the index for 'key' if 'key' is an appropriate key to live in
	 ** the array part of the table, -1 otherwise.
	 */
	private static int? ArrayIndex(StkId k) =>
		IsPositiveInteger(k) ? (int)k.V.NValue : null;

	private static readonly byte[] Log2 =
	[
		0,1,2,2,3,3,3,3,4,4,4,4,4,4,4,4,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
		6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
		7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
		7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
		8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8
	];

	private static int CeilLog2(int x)
	{
		LuaUtil.Assert(x > 0);
		var l = 0;
		x--;
		while (x >= 256) { l += 8; x >>= 8; }
		return l + Log2[x];
	}

	private static int CountInt(StkId key, Span<int> nums)
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
			{
				if (!_arrayPart[i - 1].IsNil()) lc++;
			}
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
			var n = _hashPart[i];
			if (!n.Val.IsNil()) 
			{
				ause += CountInt(n.PtrKey, nums);
				totalUse++;
			}
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

	private void Rehash(StkId k)
	{
		Span<int> nums = stackalloc int[MAXBITS + 1];
		nums.Clear();
		
		var naSize = NumUseArray(nums);
		var totalUse = naSize;
		totalUse += NumUseHash(nums, ref naSize);
		naSize += CountInt(k, nums);
		totalUse++;
		var na = ComputeSizes(nums, ref naSize);
		Resize(naSize, totalUse - na);
	}

	private void DumpParts()
	{
		ULDebug.Log("------------------ [DumpParts] enter -----------------------");
		ULDebug.Log("<< Array Part >>");
		for (var i = 0; i < _arraySize; ++i) 
		{
			var n = _arrayPart[i];
			ULDebug.Log($"i:{i} val:{n}");
		}
		ULDebug.Log("<< Hash Part >>");
		for (var i = 0; i < _hashSize; ++i) 
		{
			var n = _hashPart[i];
			var next = n.Next?.Index ?? -1;
			ULDebug.Log(
				$"i:{i} index:{n.Index} key:{n.Key} val:{n.Val} next:{next}");
		}
		ULDebug.Log("++++++++++++++++++ [DumpParts] leave +++++++++++++++++++++++");
	}
	
	private StkId NewTableKey(StkId k)
	{
		if (k.V.IsNil()) 
			L.RunError("Table index is nil");

		if (k.V.IsNumber() && double.IsNaN(k.V.NValue)) 
			L.RunError("Table index is NaN");

		var node = GetHashNode(k);

		// If main position is taken
		if (!node.Val.IsNil() || node == Dummy.Node)
		{
			var n = GetFreePos();
			if (n == null) 
			{
				Rehash(k);
				return !Get(k, out var cell) ? NewTableKey(k) : cell;
			}

			LuaUtil.Assert(n != Dummy.Node);
			var otherN = GetHashNode(node.PtrKey);
			// Is colliding node out of its main position?
			if (otherN != node) 
			{
				while (otherN.Next != node) otherN = otherN.Next!;
				otherN.Next = n;
				n.CopyFrom(node);
				node.Next = null;
				node.Val.SetNil();
			}
			// Colliding node is in its own main position
			else 
			{
				n.Next = node.Next;
				node.Next = n;
				node = n;
			}
		}

		node.Key.SetObj(k);
		LuaUtil.Assert(node.Val.IsNil());
		return node.PtrVal;
	}

	private int UnboundSearch(uint j)
	{
		var i = j;
		j++;
		while (TryGetInt((int)j, out var v) && !v.V.IsNil()) 
		{
			i = j;
			j *= 2;

			// Overflow?
			if (j <= LuaLimits.MAX_INT) continue;

			// Table was built with bad purposes: resort to linear search
			i = 1;
			while (TryGetInt((int)i, out var v2) && !v2.V.IsNil()) i++;
			return (int)(i - 1);
		}
		// Now do a binary search between them
		while (j - i > 1) 
		{
			var m = (i + j) / 2;
			if (!TryGetInt((int)m, out var v) || v.V.IsNil()) j = m;
			else i = m;
		}
		return (int)i;
	}
}