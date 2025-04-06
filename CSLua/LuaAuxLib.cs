using System.Diagnostics;

namespace CSLua;

public readonly record struct NameFuncPair(
	string Name, CsDelegate Func);

public interface ILuaAuxLib
{
	void 	Where(int level);
	int 	Error(string fmt, params object[] args);
	void	CheckStack(int size, string? msg = null);
	void 	CheckAny(int nArg);
	void 	CheckType(int index, LuaType t);
	double	CheckNumber(int nArg);
	long	CheckInt64(int nArg);
	int 	CheckInteger(int nArg);
	bool 	CheckBoolean(int nArg);
	string 	CheckString(int nArg);
	LuaTable 	CheckTable(int nArg);
	uint	CheckUnsigned(int nArg);
	object	CheckUserData(int nArg);
	List<TValue>	CheckList(int nArg);
	LuaClosure	CheckLuaFunction(int nArg);
	void 	ArgCheck(bool cond, int nArg, string extraMsg);
	int 	ArgError(int nArg, string extraMsg);
	string 	TypeName(int index);

	string 	ToStringX(int index);
	bool 	GetMetaField(int index, string method);
	int 	GetSubTable(int index, string fname);

	void 	Require(string moduleName, CsDelegate openFunc, bool global);
	void 	OpenLibs(bool global = true);
	void 	OpenSafeLibs(bool global = true);
	void 	NewLibTable(ReadOnlySpan<NameFuncPair> define);
	void	NewLib(ReadOnlySpan<NameFuncPair> define);
	void 	SetFuncs(ReadOnlySpan<NameFuncPair> define, int nup);
		
	T 		Opt<T>(Func<int,T> f, int n, T def);
	int		OptInt(int nArg, int def);
	bool	OptBoolean(int nArg, bool def);
	string 	OptString(int nArg, string def);
	bool 	CallMeta(int obj, string name);
	void	Traceback(ILuaState otherLua, string? msg = null, int level = 0);
	int		Len(int index);

	ThreadStatus LoadBuffer(string s, string? name = null);
	ThreadStatus LoadBufferX(string s, string? name = null, string? mode = null);
	ThreadStatus LoadFile(string? filename);
	ThreadStatus LoadFileX(string? filename, string? mode);

	ThreadStatus LoadString(string s, string? name = null);
	ThreadStatus LoadBytes(byte[] bytes, string name);
	ThreadStatus DoString(string s, string? name = null);
	ThreadStatus DoFile(string filename);

	string	Gsub(string src, string pattern, string rep);

	// reference system
	int		RefTo(int t);
	void	Unref(int t, int reference);
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