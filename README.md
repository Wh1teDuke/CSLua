# CSLua

## Example

```cs
public void Test1()
{
    var L = new LuaState();
    L.L_OpenLibs();
    
    L.PushCSharpFunction(FromCS);
    L.SetGlobal("FromCS");
    
    L.L_DoString(
        """
        local a, b = 1, 2
        return FromCS(a, b);
        """);

    var b = L.PopInteger();
    var a = L.PopInteger();
    Assert.Equal(2, a);
    Assert.Equal(1, b);
}

private static int FromCS(ILuaState L)
{
    var a = L.ToInteger(1);
    var b = L.ToInteger(2);
    L.PushInteger(a + 1);
    L.PushInteger(b - 1);
    return 2;
}
```

Lua 5.2 implemented in pure C#. For more examples, check out the [`Test` folder](Test/).

---

## About CSLua

**CSLua** is fork of [UniLua](https://github.com/xebecnan/UniLua). I needed a Lua interpreter compatible with [AoT C#](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) and as efficient as possible. Unfortunately most libraries don't make the cut due to being focused on unity or allocating too much garbage.
This project is a big refactoring of the original code. Xebecnan's UniLua targets version 5.2 but it also includes some 5.3 features such as utf8 support and integers.

In specific, the differences between my fork and the original are:

* **Code refactoring**: Remove references to unity, create new folders.
* **Performance improvements**: Converted code to use structs and spans where it makes sense. For example, `StkId` is no longer a class but a ref a `TValue`.
* **Added Tests**: Including some of the basic [Lua 5.2 test suite](https://www.lua.org/tests/).
* **Implement some functions of the standard library that were missing**: This is still incomplete.
* **Added the usual power patches**: Compound assignments (`+=`, `-=`, `..=` and friends), `continue`, `!=` alias for `~=`, digit separators (`1_000`).

> [!WARNING]
> While this library is functional, expect bugs. Some of lua's tests were disabled due to differences in the implementation or incomplete standard library. You can find them by grepping **CSLUA_FAIL** in the [test/suite folder](Test/suite). Don't expect a stable API.


Pull requests are welcome.