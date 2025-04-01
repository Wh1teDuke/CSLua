using CSLua;
using CSLua.Extensions;

// ReSharper disable All

namespace Test;

public sealed class TestPushPop
{
    [Fact]
    public void TestPushInteger()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const int n1 = 1;
        L.PushInteger(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.ToInteger(-1);
        
        Assert.Equal(1, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushUInt64()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const long n1 = 1;
        L.PushInt64(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.ToInt64(-1);
        
        Assert.Equal(1, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushDouble()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const double n1 = 1.0;
        L.PushNumber(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.ToNumber(-1);
        
        Assert.Equal(1, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushBool()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const bool n1 = true;
        L.PushBoolean(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.ToBoolean(-1);
        
        Assert.Equal(1, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushString()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const string n1 = "foobar";
        L.PushString(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.ToString(-1);
        
        Assert.Equal(1, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushObject()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        var n1 = new object();
        L.PushLightUserData(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.ToUserData(-1);
        
        Assert.Equal(1, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushMulti()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());

        var i1 = 6;
        var i2 = 7.0;
        var i3 = true;
        var i4 = "foobar";
        var i5 = new object();
        var i6 = 8L;

        L.PushInteger(i1);
        L.PushNumber(i2);
        L.PushBoolean(i3);
        L.PushString(i4);
        L.PushLightUserData(i5);
        L.PushInt64(i6);
        Assert.Equal(6, L.GetTop());
        
        var n1 = L.ToInteger(1);
        var n2 = L.ToNumber(2);
        var n3 = L.ToBoolean(3);
        var n4 = L.ToString(4);
        var n5 = L.ToUserData(5);
        var n6 = L.ToInt64(6);
        
        Assert.Equal(6, L.GetTop());
        Assert.Equal(i1, n1);
        Assert.Equal(i2, n2);
        Assert.Equal(i3, n3);
        Assert.Equal(i4, n4);
        Assert.Equal(i5, n5);
        Assert.Equal(i6, n6);
    }
    
    [Fact]
    public void TestPushPopUInt64()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const long n1 = 1;
        L.PushInt64(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.PopInt64();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushPopInteger()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const int n1 = 1;
        L.PushInteger(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.PopInteger();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushPopDouble()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const double n1 = 1.0;
        L.PushNumber(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.PopNumber();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushPopBool()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const bool n1 = true;
        L.PushBoolean(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.PopBool();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushPopString()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        const string n1 = "foobar";
        L.PushString(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.PopString();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushPopObject()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());
        
        var n1 = new object();
        L.PushLightUserData(n1);
        Assert.Equal(1, L.GetTop());
        var n2 = L.PopUserData();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(n1, n2);
    }
    
    [Fact]
    public void TestPushPopMulti()
    {
        var L = new LuaState();
        Assert.Equal(0, L.GetTop());

        var i1 = 1;
        var i2 = 1.0;
        var i3 = true;
        var i4 = "foobar";
        var i5 = new object();

        L.PushInteger(i1);
        Assert.Equal(1, L.GetTop());
        L.PushNumber(i2);
        Assert.Equal(2, L.GetTop());
        L.PushBoolean(i3);
        Assert.Equal(3, L.GetTop());
        L.PushString(i4);
        Assert.Equal(4, L.GetTop());
        L.PushLightUserData(i5);
        Assert.Equal(5, L.GetTop());
        
        var n5 = L.PopUserData();
        Assert.Equal(4, L.GetTop());
        var n4 = L.PopString();
        Assert.Equal(3, L.GetTop());
        var n3 = L.PopBool();
        Assert.Equal(2, L.GetTop());
        var n2 = L.PopNumber();
        Assert.Equal(1, L.GetTop());
        var n1 = L.PopInteger();
        
        Assert.Equal(0, L.GetTop());
        Assert.Equal(i1, n1);
        Assert.Equal(i2, n2);
        Assert.Equal(i3, n3);
        Assert.Equal(i4, n4);
        Assert.Equal(i5, n5);
    }

    [Fact]
    public void SetGlobalInteger()
    {
        var L = new LuaState();
        var i1 = 1;
        
        L.SetGlobalInteger("foo", i1);
        Assert.Equal(0, L.GetTop());
        
        var i2 = L.GetGlobalInteger("foo");
        Assert.Equal(i1, i2);
        Assert.Equal(0, L.GetTop());
    }
    
    [Fact]
    public void SetGlobalNumber()
    {
        var L = new LuaState();
        var i1 = 1.0;
        
        L.SetGlobalNumber("foo", i1);
        Assert.Equal(0, L.GetTop());
        
        var i2 = L.GetGlobalNumber("foo");
        Assert.Equal(i1, i2);
        Assert.Equal(0, L.GetTop());
    }
}