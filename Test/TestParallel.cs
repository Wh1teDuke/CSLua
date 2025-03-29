using CSLua;
using CSLua.Extensions;

namespace Test;

public sealed class TestParallel
{
    private static int _acc;
    
    [Fact]
    public void Test1()
    {
        var threads = new Thread[Environment.ProcessorCount];
        for (var i = 0; i < Environment.ProcessorCount; i++)
        {
            threads[i] = new Thread(Run1);
            threads[i].Start();
        }

        foreach (var thread in threads)
            thread.Join();
        
        Assert.Equal(12525000 * Environment.ProcessorCount, _acc);
    }

    private static void Run1()
    {
        var L = new LuaState();
        L.L_OpenLibs();
        L.L_DoString("""
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

        var test = L.ToLuaFunction(-1);
        L.Pop(1);
        var res = 0;
        for (var i = 0; i < 100; i++)
        {
            L.PushLuaFunction(test);
            L.Call(0, 1);
            var r = L.PopInteger();
            Assert.Equal(125250, r);
            Assert.Equal(0, L.GetTop());
            res += 125250;
        }

        Interlocked.Add(ref _acc, res);
    }
}