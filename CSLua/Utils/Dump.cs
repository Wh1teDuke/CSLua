
// ReSharper disable InconsistentNaming
namespace CSLua.Utils;

public enum DumpStatus { OK, ERROR, }

public delegate DumpStatus LuaWriter(byte[] bytes, int start, int length);

internal sealed class DumpState
{
	public static DumpStatus Dump(
		LuaProto proto, LuaWriter writer, bool strip)
	{
		var d = new DumpState(writer)
		{ _strip = strip, _status = DumpStatus.OK };

		d.DumpHeader();
		d.DumpFunction(proto);

		return d._status;
	}

	private readonly LuaWriter 	_writer;
	private bool		_strip;
	private DumpStatus	_status;

	private const string LUAC_TAIL = "\u0019\u0093\r\n\u001a\n";
	private static readonly int VERSION = (LuaDef.LUA_VERSION_MAJOR[0]-'0') * 16 + 
	                                      (LuaDef.LUA_VERSION_MINOR[0]-'0');
	private static readonly int LUAC_HEADERSIZE = LuaConf.LUA_SIGNATURE.Length +
	                                              2 + 6 + LUAC_TAIL.Length;
	private const int FORMAT = 0;
	private const int ENDIAN = 1;

	private DumpState(LuaWriter writer) => _writer = writer;

	private static byte[] BuildHeader()
	{
		var bytes = new byte[LUAC_HEADERSIZE];
		var i = 0;

		foreach (var t in LuaConf.LUA_SIGNATURE)
			bytes[i++] = (byte)t;

		bytes[i++] = (byte)VERSION;
		bytes[i++] = FORMAT;
		bytes[i++] = ENDIAN;
		bytes[i++] = 4; // sizeof(int)
		bytes[i++] = 4; // sizeof(size_t)
		bytes[i++] = 4; // sizeof(Instruction)
		bytes[i++] = sizeof(double); // sizeof(lua_Number)
		bytes[i++] = 0; // is lua_Number integral?

		foreach (var t in LUAC_TAIL)
			bytes[i++] = (byte)t;

		return bytes;
	}

	private void DumpHeader()
	{
		var bytes = BuildHeader();
		DumpBlock(bytes);
	}

	private void DumpBool(bool value) => DumpByte(value ? (byte)1 : (byte) 0);

	private void DumpInt(int value) => DumpBlock(BitConverter.GetBytes(value));

	private void DumpUInt(uint value) => DumpBlock(BitConverter.GetBytes(value));

	private void DumpString(string? value)
	{
		if (value == null)
		{
			DumpUInt(0);
		}
		else
		{
			DumpUInt((uint)(value.Length + 1));
			foreach (var t in value)
				DumpByte((byte)t);
			DumpByte((byte)'\0');
		}
	}

	private void DumpByte(byte value)
	{
		var bytes = new[] { value };
		DumpBlock(bytes);
	}

	private void DumpCode(LuaProto proto)
	{
		DumpVector(proto.Code, (ins) =>
			DumpBlock(BitConverter.GetBytes((uint)ins)));
	}

	private void DumpConstants(LuaProto proto)
	{
		DumpVector(proto.K, (k) => 
		{
			var t = k.Tt;
			DumpByte((byte)t);
			switch (t)
			{
				case (int)LuaType.LUA_TNIL:
					break;
				case (int)LuaType.LUA_TBOOLEAN:
					DumpBool(k.BValue());
					break;
				case (int)LuaType.LUA_TUINT64:
				case (int)LuaType.LUA_TNUMBER:
					DumpBlock(BitConverter.GetBytes(k.NValue));
					break;
				case (int)LuaType.LUA_TSTRING:
					DumpString(k.SValue());
					break;
				default:
					Util.Assert(false);
					break;
			}
		});

		DumpVector(proto.P, DumpFunction);
	}

	private void DumpUpvalues(LuaProto proto)
	{
		DumpVector(proto.Upvalues, (upVal) => 
		{
			DumpByte(upVal.InStack ? (byte)1 : (byte)0);
			DumpByte((byte)upVal.Index);
		});
	}

	private void DumpDebug(LuaProto proto)
	{
		DumpString(_strip ? null : proto.Source);
		DumpVector((_strip ? null : proto.LineInfo), DumpInt);
		DumpVector((_strip ? null : proto.LocVars), (locvar) => 
		{
			DumpString(locvar.VarName);
			DumpInt(locvar.StartPc);
			DumpInt(locvar.EndPc);
		});
		DumpVector((_strip ? null : proto.Upvalues), 
			(upval) => DumpString(upval.Name));
	}

	private void DumpFunction(LuaProto proto)
	{
		DumpInt(proto.LineDefined);
		DumpInt(proto.LastLineDefined);
		DumpByte((byte)proto.NumParams);
		DumpByte(proto.IsVarArg ? (byte)1 : (byte)0);
		DumpByte(proto.MaxStackSize);
		DumpCode(proto);
		DumpConstants(proto);
		DumpUpvalues(proto);
		DumpDebug(proto);
	}

	private delegate void DumpItemDelegate<in T>(T item);

	private void DumpVector<T>(IList<T>? list, DumpItemDelegate<T> dumpItem)
	{
		if (list == null)
		{
			DumpInt(0);
		}
		else
		{
			DumpInt(list.Count);
			foreach (var t in list)
			{
				dumpItem(t);
			}
		}
	}

	private void DumpBlock(byte[] bytes) => 
		DumpBlock(bytes, 0, bytes.Length);

	private void DumpBlock(byte[] bytes, int start, int length)
	{
		if (_status == DumpStatus.OK) 
			_status = _writer(bytes, start, length);
	}
}