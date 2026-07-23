using CSLua.Util;

// ReSharper disable InconsistentNaming

namespace CSLua.Extensions;

public static class LuaExtensions
{
    extension(LuaState L)
    {
        /// <summary>
        /// Pops the integer variable from the top of the stack, then returns it.
        /// </summary>
        /// <remarks>
        /// If the variable is not an integer (for example, a double or a string),
        /// the engine will try to cast or parse it if possible.
        /// </remarks>
        public int PopInteger()
        {
            var i = L.ToInteger(-1);
            L.Pop(1);
            return i;
        }

        public long PopInt64()
        {
            var i = L.ToInt64(-1);
            L.Pop(1);
            return i;
        }

        public double PopNumber()
        { // TODO: Return double? for consistency
            var i = L.ToNumber(-1);
            L.Pop(1);
            return i;
        }

        public bool? PopBool()
        {
            var i = L.ToBoolean(-1);
            L.Pop(1);
            return i;
        }

        public string? PopString()
        {
            var i = L.ToString(-1);
            if (i != null) L.Pop(1);
            return i;
        }

        public LuaTable PopTable()
        {
            var i = (LuaTable)L.ToObject(-1)!;
            L.Pop(1);
            return i;
        }

        public object? PopUserData()
        {
            var i = L.ToUserData(-1);
            if (i != null) L.Pop(1);
            return i;
        }

        public LuaState? PopThread()
        {
            var t = L.ToThread(-1);
            if (t != null) L.Pop(1);
            return t;
        }

        public LuaClosure? PopLuaClosure()
        {
            var t = L.ToLuaClosure(-1);
            if (t != null) L.Pop(1);
            return t;
        }

        public void SetGlobal(string name, Lua.CsDelegate closure)
        {
            L.PushCsDelegate(closure);
            L.SetGlobal(name);
        }

        public void SetGlobal(string name, int i)
        {
            L.PushInteger(i);
            L.SetGlobal(name);
        }

        public int? GetGlobalInteger(string name)
        {
            L.GetGlobal(name);
            return L.PopInteger();
        }

        public void SetGlobal(string name, double i)
        {
            L.PushNumber(i);
            L.SetGlobal(name);
        }

        public double GetGlobalNumber(string name)
        {
            L.GetGlobal(name);
            return L.PopNumber();
        }

        public void SetGlobal(string name, bool value)
        {
            L.PushBoolean(value);
            L.SetGlobal(name);
        }

        public bool? GetGlobalBool(string name)
        {
            L.GetGlobal(name);
            return L.PopBool();
        }

        public bool? TryGetBool(int index) => 
            !L.IsBool(index) ? null : L.ToBoolean(-1);

        public bool? TryPopBool()
        {
            var r = L.TryGetBool(-1);
            if (r.HasValue) L.Pop(1);
            return r;
        }

        public double? TryGetNumber(int index) => 
            !L.IsNumber(index) ? null : L.ToNumber(-1);

        public double? TryPopNumber()
        {
            var r = L.TryGetNumber(-1);
            if (r.HasValue) L.Pop(1);
            return r;
        }

        public bool PrintAnyError() => L.PrintAnyError(L.Status);

        public string PopErrorMsg()
        {
            var err = L.ToStringX(-1);
            L.Pop(-1);
            return err;
        }

        public bool PrintAnyError(ThreadStatus status)
        {
            if (status != ThreadStatus.LUA_OK)
            {
                L.PrintError();
                return true;
            }

            return false;
        }

        public void PrintError()
        {
            var err = L.PopErrorMsg();
            Console.WriteLine("Error!: " + err);
        }

        public void RegisterFunction(string name, Lua.CsDelegate callBack)
        {
            L.PushCsDelegate(callBack);
            L.SetGlobal(name);
        }

        public void DeleteGlobal(string name)
        {
            L.GetGlobal(name);
            L.PushNil();
            L.SetGlobal(name);
            L.Pop(-1);
        }

        public void DeleteField(string name, string field)
        {
            L.GetGlobal(name);
            L.PushNil();
            L.SetField(-2, field);
            L.Pop(-1);
        }

        private void EvalX(string s, BaseClosure? errorHandler = null)
        {
            ThreadStatus status;
            var popCount = 1;
		
            if (errorHandler == null)
                status = L.DoString(s);

            else
            {
                L.PushClosure(errorHandler);
                var errIndex = L.GetTop();
                status = L.LoadString(s);
                popCount++;

                if (status == ThreadStatus.LUA_OK) 
                    status = L.PCall(0, LuaDef.LUA_MULTRET, errIndex);
            }
		
            if (status == ThreadStatus.LUA_OK) return;

            var msg = L.ToString(-1)!;
            L.Pop(popCount);
            throw new LuaRuntimeException(status, msg);
        }

        public void Eval(string s, BaseClosure? onError = null) =>
            L.EvalX(s, onError);

        public void Eval(string s, Lua.CsDelegate onError) =>
            L.EvalX(s, new CsClosure(onError));

        private void PushClosure(BaseClosure c)
        {
            if (c is LuaClosure closure) L.PushLuaClosure(closure);
            else L.PushCsClosure((CsClosure)c);
        }

        public bool TestStack(ReadOnlySpan<Lua.Type> args)
        {
            if (L.GetTop() != args.Length) return false;
            var i = 1;
            foreach (var arg in args)
            {
                if (L.Type(i++) != arg) return false;
            }

            return true;
        }
    }
}