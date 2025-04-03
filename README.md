# CSLua

## Example

```cs
public void Test()
{
    var L = new LuaState();
    L.OpenLibs(); // or L.OpenSafeLibs();
    
    L.PushCsDelegate(FromCS);
    L.SetGlobal("FromCS");
    
    L.DoString(
        """
        assert(_CSLUA)
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
This project is a big refactoring of the original code. [@Xebecnan](https://github.com/xebecnan)'s UniLua targets version 5.2 but it also includes some 5.3 features such as utf8 support and integers.

In specific, the differences between my fork and the original are:

* **Code refactoring**: Remove references to unity, create new folders.
* **Performance improvements**: Converted code to use structs and spans where it makes sense. For example, `StkId` is no longer a class but a ref to `TValue`.
* **Added Tests**: Including some of the basic [Lua 5.2 test suite](https://www.lua.org/tests/).
* **Implement some functions of the standard library that were missing**: This is still incomplete.
* **Added some [power patches](Test/TestPatches.cs)**: Compound assignments (`+=`, `-=`, `..=` and friends), `continue`, `!=` alias for `~=`, digit separators (`1_000`), implicit pairs (`for i, v in {1, 2, 3} do ...`).
* **First class citizen support for [lists](Test/TestList.cs)**: 
```lua
local list = require 'list'
local list1 = list.new(1, 2, 3)
list1[0] = 2
assert(list1[0] == 2)
assert(#list1 == 3)
list.add(list1, 4)
assert(#list1 == 4)
```

> [!WARNING]
> While this library is functional, expect some bugs. Some of lua's tests were disabled due to differences in the implementation or incomplete standard library. You can find them by grepping **CSLUA_FAIL** in the [test/suite folder](Test/suite). Don't expect a stable API.


Contributions are welcome.