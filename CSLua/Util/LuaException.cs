namespace CSLua.Util;


public class LuaException(string msg) : Exception(msg);

public class LuaRuntimeException(
    ThreadStatus errCode, string msg) : LuaException(msg)
{
    public ThreadStatus ErrCode { get; } = errCode;
}