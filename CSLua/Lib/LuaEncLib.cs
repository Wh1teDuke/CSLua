using System.Text;
using CSLua.Utils;

namespace CSLua.Lib;

public static class LuaEncLib
{
	public const string LIB_NAME = "enc";
	private const string EncUtf8 = "utf8";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("encode", ENC_Encode),
			new("decode", ENC_Decode),
		];

		lua.L_NewLib(define);

		lua.PushString(EncUtf8);
		lua.SetField(-2, "utf8");

		return 1;
	}

	private static int ENC_Encode(ILuaState lua)
	{
		var s = lua.ToString(1);
		var e = lua.ToString(2);
		if (e != EncUtf8)
			throw new LuaException("Unsupported encoding:" + e);

		var bytes = Encoding.UTF8.GetBytes(s);
		var sb = new StringBuilder();
		foreach (var t in bytes) sb.Append((char)t);
		lua.PushString(sb.ToString());
		return 1;
	}

	private static int ENC_Decode(ILuaState lua)
	{
		var s = lua.ToString(1);
		var e = lua.ToString(2);
		if (e != EncUtf8)
			throw new LuaException("unsupported encoding:" + e);

		var bytes = new byte[s.Length];
		for (var i = 0; i < s.Length; ++i) bytes[i] = (byte)s[i];

		lua.PushString(Encoding.UTF8.GetString(bytes));
		return 1;
	}
}