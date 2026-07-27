using CSLua;
using CSLua.Extensions;
using Xunit.Sdk;

namespace Test;

public sealed class TestParallel
{
    private static int _acc;
    private static readonly List<XunitException> _exceptions = [];
    private static readonly Lock _lock = new ();
    
    [Fact]
    public void Test1()
    {
        _exceptions.Clear();
        _acc = 0;
        
        var threads = new Thread[Environment.ProcessorCount];
        for (var i = 0; i < Environment.ProcessorCount; i++)
        {
            threads[i] = new Thread(Run1);
            threads[i].Start();
        }

        foreach (var thread in threads)
            thread.Join();
        
        if (_exceptions.Count != 0)
            throw new AggregateException(_exceptions);
        
        Assert.Equal(12_525_000 * Environment.ProcessorCount, _acc);
    }

    private static void Run1()
    {
        var L = Lua.New();
        L.OpenLibs();
        L.DoString("""
                     return function()
                        local t = {}
                        local r = 0
                        for i = 1, 500 do
                            table.insert(t, i)
                        end
                        for i, v in pairs(t) do
                          r = r + v
                        end
                        for i = 500, 1, -1 do
                         table.remove(t, i)
                        end
                        return r
                     end
                     """);

        var test = L.CheckLuaFunction(-1);
        L.Pop(1);
        var res = 0;
        for (var i = 0; i < 100; i++)
        {
            L.PushLuaClosure(test);
            L.Call(0, 1);
            var r = L.PopInteger();
            try
            {
                Assert.Equal(125250, r);
                Assert.Equal(0, L.GetTop());
            }
            catch (XunitException e)
            {
                using var _ = _lock.EnterScope();
                _exceptions.Add(e);
                return;
            }

            res += 125250;
        }

        Interlocked.Add(ref _acc, res);
    }
}