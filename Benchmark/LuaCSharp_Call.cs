using BenchmarkDotNet.Attributes;
using CSLua.Extensions;
using Lua;
using Lua.Runtime;
using Lua.Standard;

namespace Benchmark;

[MemoryDiagnoser]
public class LuaCSharp_Call
{
    
    private const string CodeAdd = """
                                   local x = 0

                                   for _ = 0, 25000 do
                                       x = add(x, 1)
                                   end

                                   return x
                                   """;
    
    private CSLua.LuaState _csLua = null!;
    private CSLua.LuaClosure _csLuaClosure = null!;
    private LuaState _luaCs = null!;
    private LuaClosure _luaCsClosure = null!;

    [GlobalSetup]
    public void Setup()
    {
        // CSLua
        _csLua = new CSLua.LuaState();
        _csLua.OpenLibs();
        _csLua.SetGlobal("add", L =>
        {
            var a = L.ToNumber(1);
            var b = L.ToNumber(2);
            L.PushNumber(a + b);
            return 1;
        });
        _csLuaClosure = _csLua.Compile(CodeAdd);

        // Lua-CSharp
        _luaCs = LuaState.Create();
        _luaCs.OpenStandardLibraries();
        _luaCs.Environment["add"] = new LuaFunction(
            "add",
            (context, ct) =>
            {
                var a = context.GetArgument<double>(0);
                var b = context.GetArgument<double>(1);
                return new(context.Return(a + b));
            }
        );
        _luaCsClosure = _luaCs.Load(CodeAdd, "CodeAdd");
    }
    
    [IterationCleanup]
    public void Cleanup() => GC.Collect();

    [Benchmark]
    public async ValueTask RunLuaCsharp() =>
        await _luaCs.CallAsync(_luaCsClosure, []);

    [Benchmark]
    public void RunCsLua() =>
        _csLua.Call(_csLuaClosure);
}