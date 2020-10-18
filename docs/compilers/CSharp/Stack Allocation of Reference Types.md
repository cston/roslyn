Allow stack allocation of reference type instances that do not escape the allocation scope.

### Proposal
Classes and methods can be annotated with a `[DoesNotEscape]` attribute to indicate that references to receivers or parameters do not escape the corresponding methods.

_The attribute `[DoesNotEscape]` is a placeholder. It might be replaced by specific syntax, such as a `&` modifier, particularly if annotations should be supported on locals or expressions._

`[DoesNotEscape]` on a class indicates that references to `this` do not escape any of the instance methods. `[DoesNotEscape]` on a method parameter indicates that the parameter does not escape the method.
The compiler will use escape analysis in those methods and report errors when references to annotated parameters or `this` escape the method.
That includes reporting errors when annotated parameters or `this` are used as receivers or arguments to methods that are not annotated.

`[DoesNotEscape]` can only be used on a class if all base classes are also marked `[DoesNotEscape]`.
Adding `[DoesNotEscape]` to a base class does not affect unannotated derived classes: methods in unannotated derived classes, including overrides of base class methods, are assumed to allow `this` and parameters to escape.
That allows `[DoesNotEscape]` to be added to existing base classes incrementally without breaking derived classes.

When an annotated reference type is allocated, implicitly or explicitly, and the compiler can verify the allocated instance does not escape the containing scope, the compiler will emit a hint to the runtime that the allocation can occur on the stack.

`stackalloc` can be used to force an allocation to occur on the stack.
`stackalloc` will be allowed wherever `new` expressions are allowed. The expression `stackalloc T(...)` will be emitted as a stack allocation of `T` if `T` is a reference type and the resulting reference does not escape the current scope; otherwise a compile error is reported.

`stackalloc` will also be allowed with lambda expressions.
The expression `stackalloc () => { ... })` will be emitted as a stack allocation of the delegate and any closure class instance if the resulting delegate does not escape the current scope; otherwise a compile error is reported.

### Examples

Stack allocation may be used implicitly or explicitly for reference type allocations.
```C#
namespace System
{
    [DoesNotEscape]
    class Object { ... }
}

class HeapOnly { ... }

[DoesNotEscape]
class HeapOrStack { ... }

class Program
{
    static void DoSomething<T>([DoesNotEscape] T t) { }

    static void Main()
    {
        DoSomething(new HeapOnly());           // allocated on heap
        DoSomething(stackalloc HeapOnly());    // error
        DoSomething(new HeapOrStack());        // allocated on heap or stack
        DoSomething(stackalloc HeapOrStack()); // allocated on stack
    }
} 
```

Stack allocation may be used implicitly or explicitly for `params` array arguments.
```C#
static void Format(string format, [DoesNotEscape]params object[] args) { ... }

static void Main()
{
    Format("{0}", new HeapOnly());                   // object[] allocated on heap or stack
    Format("{0}", stackalloc[] { new HeapOnly() })); // object[] allocated on stack
}
```

Stack allocation may be used implicitly or explicitly for delegates and closures.
```C#
static List<T> Where<T>(List<T> list, [DoesNotEscape]Func<T, bool> predicate) { ... }

static List<int> GreaterThan(List<int> list, int value)
{
    return Where(list, stackalloc item => item > value); // closure and delegate allocated on stack
}
```

_The above example is contrived. More realistically, `Where()` would be declared as `static IEnumerable<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)`.
We should handle that case where the predicate might escape the call to `Where` since it will end up in the constructed result instance,
but as long as the caller limits the use of the lifetime of the result to the lifetime of the source, the predicate (and even the result) could be allocated on the stack._

### Possible refinement: Instance methods
The above approach requires all instance methods of a class (and base classes) to prevent `this` from escaping before the type can be allocated on the stack.
We could relax that restriction and allow individual methods to be marked `[DoesNotEscape]`.
Then the type could be allocated on the stack if the compiler can verify that the reference is only passed to annotated methods.
This would allow stack allocation in more scenarios at the cost of additional verification at the allocation site.

The constructor used in the allocation would need to be marked `[DoesNotEscape]`. No annotation would be needed for the default parameterless constructor however.

For calls to virtual methods and methods from interface implementations, escape analysis within a method body could verify that the definition of the method on the base class or interface is annotated, and
escape analysis at the allocation site could verify that _all implementations of virtual methods_ from the base type to the allocated type are also annotated.

```C#
abstract class A
{
    [DoesNotEscape] abstract void F1();
    virtual void F2() { }
}
class B1 : A
{
    [DoesNotEscape] override void F1() { }
    override void F2() { }
}
class B2 : A
{
    override void F1() { }
}
class Program
{
    static void DoSomething([DoesNotEscape] A a)
    {
    }
    static void Main()
    {
        DoSomething(new B1()); // allocated on heap or stack
        DoSomething(new B2()); // allocated on heap: B2.F1 is not annotated
    }
} 
```

### See also

See [roslyn/issues/2104](https://github.com/dotnet/roslyn/issues/2104), [coreclr/issues/1784](https://github.com/dotnet/coreclr/issues/1784)

