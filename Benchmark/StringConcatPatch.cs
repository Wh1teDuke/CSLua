using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CSLua;
using CSLua.Extensions;

namespace Benchmark;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class StringConcatPatch
{
    private LuaState _luaState = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        _luaState = Lua.New();
        
        _luaState.DoString("function a2() return 'foo' .. 'bar' end");
        _luaState.DoString("function b2() return 'foo' +  'bar' end");

        _luaState.DoString("function a5() return 'foo' .. 'bar' .. true .. 1 .. false end");
        _luaState.DoString("function b5() return 'foo' +  'bar' +  true +  1 +  false end");
    }
    
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Concat2")]
    public string ConcatClassic2()
    {
        _luaState.GetGlobal("a2");
        _luaState.Call(0, 1);
        return _luaState.PopString()!;
    }
    
    [Benchmark]
    [BenchmarkCategory("Concat2")]
    public string ConcatPatch2()
    {
        _luaState.GetGlobal("b2");
        _luaState.Call(0, 1);
        return _luaState.PopString()!;
    }
    
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Concat5")]
    public string ConcatClassic5()
    {
        _luaState.GetGlobal("a5");
        _luaState.Call(0, 1);
        return _luaState.PopString()!;
    }
    
    [Benchmark]
    [BenchmarkCategory("Concat5")]
    public string ConcatPatch5()
    {
        _luaState.GetGlobal("b5");
        _luaState.Call(0, 1);
        return _luaState.PopString()!;
    }
}