
// #define DEBUG_BINARY_READER
// #define DEBUG_UNDUMP

using CSLua.Parse;

namespace CSLua.Utils;

public sealed class BinaryBytesReader(ILoadInfo loadinfo)
{
	public int SizeOfSizeT;

	public byte[] ReadBytes(int count)
	{
		var ret = new byte[count];
		for (var i = 0; i < count; ++i)
		{
			var c = loadinfo.ReadByte();
			if (c == -1)
				throw new UnDumpException("truncated");
			ret[i] = (byte)c;
		}
#if DEBUG_BINARY_READER
			var sb = new System.Text.StringBuilder();
			sb.Append("ReadBytes:");
			for( var i=0; i<ret.Length; ++i )
			{
				sb.Append( string.Format(" {0:X02}", ret[i]) );
			}
			ULDebug.Log( sb.ToString() );
#endif
		return ret;
	}

	public int ReadInt()
	{
		var bytes = ReadBytes(4);
		var ret = BitConverter.ToInt32(bytes, 0);
#if DEBUG_BINARY_READER
			ULDebug.Log( "ReadInt: " + ret );
#endif
		return ret;
	}

	public uint ReadUInt()
	{
		var bytes = ReadBytes(4);
		var ret = BitConverter.ToUInt32(bytes, 0);
#if DEBUG_BINARY_READER
			ULDebug.Log( "ReadUInt: " + ret );
#endif
		return ret;
	}

	public int ReadSizeT()
	{
		if (SizeOfSizeT <= 0)
			throw new Exception("sizeof(size_t) is not valid: " + SizeOfSizeT);

		var bytes = ReadBytes(SizeOfSizeT);
		var ret = SizeOfSizeT switch
		{
			4 => BitConverter.ToUInt32(bytes, 0),
			8 => BitConverter.ToUInt64(bytes, 0),
			_ => throw new NotImplementedException()
		};

#if DEBUG_BINARY_READER
			ULDebug.Log( "ReadSizeT: " + ret );
#endif

		if (ret > int.MaxValue)
			throw new NotImplementedException();
		return (int)ret;
	}

	public double ReadDouble()
	{
		var bytes = ReadBytes(8);
		var ret = BitConverter.ToDouble(bytes, 0);
#if DEBUG_BINARY_READER
			ULDebug.Log( "ReadDouble: " + ret );
#endif
		return ret;
	}

	public byte ReadByte()
	{
		var c = loadinfo.ReadByte();
		if (c == -1)
			throw new UnDumpException("Truncated");
#if DEBUG_BINARY_READER
			ULDebug.Log( "ReadBytes: " + c );
#endif
		return (byte)c;
	}

	public string ReadString()
	{
		var n = ReadSizeT();
		if (n == 0) return null;

		var bytes = ReadBytes(n);

		// n=1: removing trailing '\0'
		var ret = System.Text.Encoding.UTF8.GetString(bytes, 0, n - 1);
#if DEBUG_BINARY_READER
			ULDebug.Log( "ReadString n:" + n + " ret:" + ret );
#endif
		return ret;
	}
}

internal sealed class UnDumpException(string why) : LuaException(why)
{ public readonly string Why = why; }

public sealed class UnDump
{
	private readonly BinaryBytesReader _reader;
	
	public static LuaProto LoadBinary(ILoadInfo loadInfo, string name)
	{
		try
		{
			var reader = new BinaryBytesReader(loadInfo);
			var unDump = new UnDump(reader);
			unDump.LoadHeader();
			return unDump.LoadFunction();
		}
		catch (UnDumpException e)
		{
			var msg = $"{name}: {e.Why} precompiled chunk";
			throw new LuaRuntimeException(ThreadStatus.LUA_ERRSYNTAX, msg);
		}
	}

	private UnDump(BinaryBytesReader reader) => _reader = reader;

	private int LoadInt() => _reader.ReadInt();

	private byte LoadByte() => _reader.ReadByte();

	private byte[] LoadBytes(int count) => _reader.ReadBytes(count);

	private string LoadString() => _reader.ReadString();

	private bool LoadBoolean() => LoadByte() != 0;

	private double LoadNumber() => _reader.ReadDouble();

	private void LoadHeader()
	{
		var header = LoadBytes(
			4 // Signature
		        + 8 // version, format version, size of int ... etc
		        + 6 // Tail
		);
		var v = header[
			4 /* skip signature */
			+ 4 /* offset of sizeof(size_t) */
		];
#if DEBUG_UNDUMP
		ULDebug.Log(string.Format("sizeof(size_t): {0}", v));
#endif
		_reader.SizeOfSizeT = v ;
	}

	private Instruction LoadInstruction() => (Instruction)_reader.ReadUInt();

	private LuaProto LoadFunction()
	{
#if DEBUG_UNDUMP
		ULDebug.Log("LoadFunction enter");
#endif

		var proto = new LuaProto
		{
			LineDefined = LoadInt(),
			LastLineDefined = LoadInt(),
			NumParams = LoadByte(),
			IsVarArg = LoadBoolean(),
			MaxStackSize = LoadByte()
		};

		LoadCode(proto);
		LoadConstants(proto);
		LoadUpvalues(proto);
		LoadDebug(proto);
		return proto;
	}

	private void LoadCode(LuaProto proto)
	{
		var n = LoadInt();
#if DEBUG_UNDUMP
		ULDebug.Log("LoadCode n:" + n);
#endif
		proto.Code.Clear();
		for (var i = 0; i < n; ++i)
		{
			proto.Code.Add(LoadInstruction());
#if DEBUG_UNDUMP
			ULDebug.Log("Count:" + proto.Code.Count);
			ULDebug.Log("LoadInstruction:" + proto.Code[proto.Code.Count - 1]);
#endif
		}
	}

	private void LoadConstants(LuaProto proto)
	{
		var n = LoadInt();
#if DEBUG_UNDUMP
		ULDebug.Log("Load Constants:" + n);
#endif
		proto.K.Clear();
		for (var i = 0; i < n; ++i)
		{
			int t = LoadByte();
#if DEBUG_UNDUMP
			ULDebug.Log("Constant Type:" + t);
#endif
			var v = new TValue();
			switch (t)
			{
				case (int)LuaType.LUA_TNIL:
					v.SetNil();
					proto.K.Add(v);
					break;

				case (int)LuaType.LUA_TBOOLEAN:
					v.SetBool(LoadBoolean());
					proto.K.Add(v);
					break;

				case (int)LuaType.LUA_TNUMBER:
					v.SetDouble(LoadNumber());
					proto.K.Add(v);
					break;

				case (int)LuaType.LUA_TSTRING:
#if DEBUG_UNDUMP
					ULDebug.Log("LuaType.LUA_TSTRING");
#endif
					v.SetString(LoadString());
					proto.K.Add(v);
					break;

				default:
					throw new UnDumpException(
						"LoadConstants unknown type: " + t);
			}
		}

		n = LoadInt();
#if DEBUG_UNDUMP
		ULDebug.Log("Load Functions:" + n);
#endif
		proto.P.Clear();
		for (var i = 0; i < n; ++i)
		{
			proto.P.Add(LoadFunction());
		}
	}

	private void LoadUpvalues(LuaProto proto)
	{
		var n = LoadInt();
#if DEBUG_UNDUMP
		ULDebug.Log("Load Upvalues:" + n);
#endif
		proto.Upvalues.Clear();
		for (var i = 0; i < n; ++i)
		{
			proto.Upvalues.Add(
				new UpValueDesc
				{
					Name = null!,
					InStack = LoadBoolean(),
					Index = LoadByte()
				});
		}
	}

	private void LoadDebug(LuaProto proto)
	{
		proto.Source = LoadString();

		// LineInfo
		var n = LoadInt();
#if DEBUG_UNDUMP
		ULDebug.Log("Load LineInfo:" + n);
#endif
		proto.LineInfo.Clear();
		for (var i = 0; i < n; ++i)
		{
			proto.LineInfo.Add(LoadInt());
		}

		// LocalVar
		n = LoadInt();
#if DEBUG_UNDUMP
		ULDebug.Log("Load LocalVar:" + n);
#endif
		proto.LocVars.Clear();
		for (var i = 0; i < n; ++i)
		{
			proto.LocVars.Add(
				new LocVar
				{
					VarName = LoadString(),
					StartPc = LoadInt(),
					EndPc   = LoadInt(),
				});
		}

		// Upvalues' name
		n = LoadInt();
		for (var i = 0; i < n; ++i)
		{
			proto.Upvalues[i] = proto.Upvalues[i] with { Name = LoadString() };
		}
	}
}