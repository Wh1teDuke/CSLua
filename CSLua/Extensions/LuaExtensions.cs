using CSLua.Util;

// ReSharper disable InconsistentNaming

namespace CSLua.Extensions;

public static class LuaExtensions
{
    extension(LuaState L)
    {
        /// <summary>
        /// Pops the lua value from the top of the stack, then
        /// returns it or null if there is nothing at the top.
        /// </summary>
        public TValue? PopTValue()
        {
            var i = L.ToTValue(-1);
            if (i != null) L.Pop(1);
            return i;
        }
        
        /// <summary>
        /// Pops the integer value from the top of the stack, then
        /// returns it or null if the value at the top is not a number.
        /// </summary>
        /// <remarks>
        /// If the variable is not an integer (for example, a double or a string),
        /// the engine will try to cast or parse it if possible.
        /// </remarks>
        public int? PopInteger()
        {
            var i = L.ToIntegerX(-1, out var isNum);

            if (!isNum) return null;
            L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the long value from the top of the stack, then
        /// returns it or null if the value at the top is not a long.
        /// </summary>
        public long? PopInt64()
        {
            if (L.Type(-1) != Lua.Type.LUA_TINT64) return null;
            
            var i = L.ToInt64(-1);
            L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the double value from the top of the stack, then
        /// returns it or null if the value at the top is not a double.
        /// </summary>
        public double? PopDouble()
        {
            if (L.Type(-1) != Lua.Type.LUA_TNUMBER) 
                return null;

            var i = L.ToNumber(-1);
            L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the bool value from the top of the stack, then
        /// returns it or null if the value at the top is not a bool.
        /// </summary>
        public bool? PopBool()
        {
            if (L.Type(-1) != Lua.Type.LUA_TBOOLEAN) return null;
            
            var i = L.ToBoolean(-1);
            L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the string value from the top of the stack, then
        /// returns it or null if the value at the top is not a string.
        /// </summary>
        public string? PopString()
        {
            var i = L.ToString(-1);
            if (i != null) L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the table value from the top of the stack, then
        /// returns it or null if the value at the top is not a table.
        /// </summary>
        public LuaTable? PopTable()
        {
            var i = L.ToTable(-1);
            if (i != null) L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the user data value from the top of the stack, then
        /// returns it or null if the value at the top is not a user data value.
        /// </summary>
        public IUSerData? PopUserData()
        {
            var i = L.ToUserData(-1);
            if (i != null) L.Pop(1);
            return i;
        }
        
        /// <summary>
        /// Pops the light user data value from the top of the stack, then
        /// returns it or null if the value at the top is not a light user data value.
        /// </summary>
        public object? PopLightUserData()
        {
            var i = L.ToLightUserData(-1);
            if (i != null) L.Pop(1);
            return i;
        }

        /// <summary>
        /// Pops the thread value from the top of the stack, then
        /// returns it or null if the value at the top is not a thread.
        /// </summary>
        public LuaState? PopThread()
        {
            var t = L.ToThread(-1);
            if (t != null) L.Pop(1);
            return t;
        }

        /// <summary>
        /// Pops the lua closure value from the top of the stack, then
        /// returns it or null if the value at the top is not a lua closure.
        /// </summary>
        public LuaClosure? PopLuaClosure()
        {
            var t = L.ToLuaClosure(-1);
            if (t != null) L.Pop(1);
            return t;
        }
        
        public void SetGlobal(string name, object lightUserData)
        {
            L.PushLightUserData(lightUserData);
            L.SetGlobal(name);
        }
        
        public void SetGlobal(string name, IUSerData udata)
        {
            L.PushUserData(udata);
            L.SetGlobal(name);
        }

        public void SetGlobal(string name, Lua.CsDelegate closure)
        {
            L.PushCsDelegate(closure);
            L.SetGlobal(name);
        }
        
        public void SetGlobal(string name, LuaClosure closure)
        {
            L.PushLuaClosure(closure);
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
            return L.PopDouble()!.Value;
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
        
        public void Call(LuaClosure closure, int args = 0, int results = 0)
        {
            L.PushLuaClosure(closure);
            var tStatus = L.PCall(args, results);
            L.AssertNoErrors(tStatus);
        }
        
        public void Call(
            LuaClosure closure,
            ReadOnlySpan<TValue> args,
            Span<TValue> results)
        {
            L.PushLuaClosure(closure);
            foreach (var arg in args) L.PushTValue(arg);

            var tStatus = L.PCall(args.Length, results.Length);
            L.AssertNoErrors(tStatus);
            
            foreach (ref var result in results)
                result = L.PopTValue()!.Value;
            results.Reverse();
        }

        public LuaClosure Compile(string code)
        {
            var tStatus = L.LoadString(code);
            L.AssertNoErrors(tStatus);
            return L.PopLuaClosure()!;
        }

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

        private void AssertNoErrors(ThreadStatus status)
        {
            if (status == ThreadStatus.LUA_OK) return;
            var msg = L.ToString(-1)!;
            L.Pop(1);
            throw new LuaRuntimeException(status, msg);
        }
    }
}