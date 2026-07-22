using System.Diagnostics;

namespace CSLua;

public readonly record struct NameFuncPair(
	string Name, Lua.CsDelegate Func);

public interface ILoadInfo
{
	int ReadByte();
	int PeekByte();
}

public struct StringLoadInfo(string s) : ILoadInfo
{
	private int	_pos;
	public string Source => s;
	public int ReadByte() => _pos >= s.Length ? -1 : s[_pos++];
	public int PeekByte() => _pos >= s.Length ? -1 : s[_pos];
}

internal struct BytesLoadInfo(byte[] b) : ILoadInfo
{
	private int _pos;
	public int ReadByte() => _pos >= b.Length ? -1 : b[_pos++];
	public int PeekByte() => _pos >= b.Length ? -1 : b[_pos];
}

internal readonly record struct ProtoLoadInfo(LuaProto Proto) : ILoadInfo
{
	public int ReadByte() => throw new UnreachableException();
	public int PeekByte() => throw new UnreachableException();
}