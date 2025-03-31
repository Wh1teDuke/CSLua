using CSLua;
using CSLua.Parse;
using static CSLua.OpCode;
using static CSLua.Parse.Instruction;

namespace Test;

// ReSharper disable once InconsistentNaming
public sealed class TestOP
{
    // TODO: Debug consts
    
    [Fact]
    public void Test1()
    {
        Test("return 1;", [
            CreateABx(OP_LOADK, 0, 0),
            CreateAB(OP_RETURN, 0, 2),
            CreateAB(OP_RETURN, 0, 1)
        ], [1]);
    }
    
    [Fact]
    public void Test2()
    {
        Test("local foo = 1;", [
            CreateABx(OP_LOADK, 0, 0),
            CreateAB(OP_RETURN, 0, 1)
        ], [1]);
    }
    
    [Fact]
    public void Test3()
    {
        Test("foo = 1;", [
            CreateABC(OP_SETTABUP, 0, RKASK(0), RKASK(1)),
            CreateAB(OP_RETURN, 0, 1)
        ], ["foo", 1]);
    }
    
    [Fact]
    public void Test4()
    {
        Test("local bar = 1 > 0 and 5 or 6;", [
            CreateABC(OP_LT, 0, RKASK(1), RKASK(0)),
            CreateAsBx(OP_JMP, 0, 3),
            CreateABx(OP_LOADK, 0, 2),
            CreateAC(OP_TEST, 0, 1),
            CreateAsBx(OP_JMP, 0, 1),
            CreateABx(OP_LOADK, 0, 3),
            CreateAB(OP_RETURN, 0, 1)
        ], [1, 0, 5, 6]);
    }
    
    [Fact]
    public void Test5()
    {
        Test("local v = true; if v then print('true') end;", [
            CreateABC(OP_LOADBOOL, 0, 1, 0),
            CreateAC(OP_TEST, 0, 0),
            CreateAsBx(OP_JMP, 0, 3),
            CreateABC(OP_GETTABUP, 1, 0, RKASK(0)),
            CreateABx(OP_LOADK, 2, 1),
            CreateABC(OP_CALL, 1, 2, 1),
            CreateAB(OP_RETURN, 0, 1)
        ], ["print", "true"]);
    }
    
    [Fact]
    public void Test6()
    {
        Test("function foo() end;",
            [ // Main
            CreateABx(OP_CLOSURE, 0, 0),
            CreateABC(OP_SETTABUP, 0, RKASK(0), 0),
            CreateAB(OP_RETURN, 0, 1)
            ],
            [ // foo
            CreateAB(OP_RETURN, 0, 1),
            ],
            ["foo"]
        );
    }
    
    [Fact]
    public void Test7()
    {
        Test("function foo() end; local bar = foo",
            [ // Main
            CreateABx(OP_CLOSURE, 0, 0),
            CreateABC(OP_SETTABUP, 0, RKASK(0), 0),
            CreateABC(OP_GETTABUP, 0, 0, RKASK(0)),
            CreateAB(OP_RETURN, 0, 1),
            ],
            [ // foo
            CreateAB(OP_RETURN, 0, 1),
            ],
            ["foo"]
        );
    }
    
        
    [Fact]
    public void Test8()
    {
        Test("local bar=123;function foo() return #bar end; foo()",
            [ // Main
                CreateABx(OP_LOADK, 0, 0),
                CreateABx(OP_CLOSURE, 1, 0),
                CreateABC(OP_SETTABUP, 0, RKASK(1), 1),
                CreateABC(OP_GETTABUP, 1, 0, RKASK(1)),
                CreateABC(OP_CALL, 1, 1, 1),
                CreateAB(OP_RETURN, 0, 1),
            ],
            [ // foo
                CreateAB(OP_GETUPVAL, 0, 0),
                CreateAB(OP_LEN, 0, 0),
                CreateAB(OP_RETURN, 0, 2),
                CreateAB(OP_RETURN, 0, 1),
            ],
            [123, "foo"]
        );
    }

    [Fact]
    public void Test9()
    {
        Test("local t = {foo='bar'}",
        [ // Main
            CreateABC(OP_NEWTABLE, 0, 0, 1),
            CreateABC(OP_SETTABLE, 0, RKASK(0), RKASK(1)),
            CreateAB(OP_RETURN, 0, 1),
        ], ["foo", "bar"]);
    }

    [Fact]
    public void TestConst1()
    {
        Test("a = 0; b = 1; c = 2",
            [ // Main
                CreateABC(OP_SETTABUP, 0, RKASK(0), RKASK(1)),
                CreateABC(OP_SETTABUP, 0, RKASK(2), RKASK(3)),
                CreateABC(OP_SETTABUP, 0, RKASK(4), RKASK(5)),
                CreateAB(OP_RETURN, 0, 1),
            ],
            ["a", 0, "b", 1, "c", 2]
        );
    }
    
    // UTIL -------------------------------------------------------------------
    
    private static Parser Of(string src) => 
        Parser.Read(src, "Test");

    private static void Test(
        string src,
        ReadOnlySpan<Instruction> values,
        ReadOnlySpan<object> consts = default)
    {
        var parser = Of(src);
        var proto = parser.Proto;
        Assert.Empty(proto.P);
        Assert.Equal(consts.Length, proto.K.Count);
        
        var op = proto.Code;
        Assert.Equal(op.Count, values.Length);
        Assert.Equal(op, values.ToArray());
        
        var kIdx = 0;
        foreach (var k in proto.K) 
        {
            var v = consts[kIdx++];
            if (v is int vInt)
                Assert.Equal(k.NValue, vInt);
            else
                Assert.Equal(k.OValue, v);
        }
    }
    
    private static void Test(
        string src, 
        ReadOnlySpan<Instruction> values1,
        ReadOnlySpan<Instruction> values2,
        ReadOnlySpan<object> consts = default)
    {
        var parser = Of(src);
        var proto = parser.Proto;
        Assert.Single(proto.P);
        Assert.Equal(consts.Length, proto.K.Count);
        
        var op1 = proto.Code;
        var op2 = proto.P[0].Code;
        
        Assert.Equal(values1.Length, op1.Count);
        Assert.Equal(op1, values1.ToArray());
        Assert.Equal(values2.Length, op2.Count);
        Assert.Equal(op2, values2.ToArray());

        var kIdx = 0;
        foreach (var k in proto.K)
        {
            var v = consts[kIdx++];
            if (v is int vInt)
            {
                Assert.True(k.IsNumber());
                Assert.Equal(LuaType.LUA_TNUMBER, (LuaType)k.Tt);
                Assert.Equal(vInt, k.NValue);
            }
            else
            {
                Assert.Equal(LuaType.LUA_TSTRING, (LuaType)k.Tt);
                Assert.Equal(v, k.OValue);
            }
        }
    }
}