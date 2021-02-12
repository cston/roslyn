## Summary

Allow stack allocation of reference type instances that do not escape the allocation scope.

## Proposal
Classes and methods can be annotated with a `[DoesNotEscape]` attribute to indicate that references to receivers or parameters do not escape the corresponding methods.

A `[DoesNotEscape]` attribute:
- on a method indicates `this` does not escape the method,
- on a parameter indicates the parameter does not escape the containing method,
- on a class or interface indicates `this` does not escape any instance methods (equivalent to annotating all instance methods).

In an annotated method, the compiler will use escape analysis to report errors when references to annotated variables escape the containing method. That includes calls to unannotated methods with annotated variables as arguments or receiver. Use of an annotated variable through an interface or derived class other than the declared type is an error.

When a reference type is allocated, and the compiler can verify the allocated instance does not escape the containing scope, the compiler will emit a hint to the runtime that the allocation can occur on the stack.
Allocations may be explicit or implicit. Implicit allocations include boxing, `params` arrays, delegate conversions, and closure class allocations.

### Example: `params` array and boxing

Consider the call to `Console.WriteLine()` below that formats two value types, where the `ToString()` implementations are annotated with `[DoesNotEscape]`, and the `params object[] args` is annotated, and where the `params` annotation means the `args` array does not escape the method, nor do the items in the array. The `object` instances for the boxing conversions of `1` and `new MyStruct()` can be allocated on the stack, and the `object[2]` array can also be allocated on the stack.

```csharp
// Object.ToString()
[DoesNotEscape] public virtual string ToString() { ... }

// Int32.ToString(), MyStruct.ToString()
[DoesNotEscape] public override string ToString() { ... }

// Console.WriteLine()
public static void WriteLine(string format, [DoesNotEscape] params object[] args) { ... }

Console.WriteLine("{0}, {1}", 1, new MyStruct());
```

When compiling `Console.WriteLine()`, the compiler will report an error if references to `args` or items in `args` are copied outside the method. If `WriteLine()` only calls `args[i].ToString()`, analysis should succeed since `Object.ToString()` is annotated.

When compiling the caller, if the compiler determines an argument type satisfies the contract on `System.Object` - that is, methods overridden from `System.Object` have `[DoesNotEscape]` annotations that are at least as strong as those on `System.Object` - that argument can be boxed in an `object` instance on the stack.

### Example: Delegate and closure class

`ContainsTypeParameter()` below relies on a helper method `VisitType()` which invokes `predicate` on each component type in `type`. For the lambda in `ContainsTypeParameter()`, the compiler generates a closure class for `typeParameter` and an instance method on the closure class for the delegate passed to `VisitType()`. Since the `predicate` parameter in `VisitType()` is annotated with `[DoesNotEscape]`, the delegate instance created implicitly for the call and the closure class instance can be allocated on the stack.

```csharp
// Returns first component of compound 'type' that satisfies 'predicate'.
static TypeSymbol VisitType(TypeSymbol type, [DoesNotEscape] Func<TypeSymbol, bool> predicate) { ... }

// Returns true if 'type' contains 'typeParameter'.
static bool ContainsTypeParameter(TypeSymbol type, TypeParameterSymbol typeParameter)
{
    return VisitType(type, t => t == typeParameter) != null;
}
```

### Opt-out
It should be possible to opt-out from (or opt-in to) runtime stack allocation of reference types.
Perhaps there is a `[MethodImpl(MethodImplOptions.NoStackReferenceTypes)]` attribute to opt-out in the calling method for instance.

### Require stack allocation
It should be possible to assert that allocations occur on the stack rather than leaving that decision to the compiler and runtime.
Perhaps `stackalloc` rather than `new` could be allowed for those cases, at least for explicit allocations.
`stackalloc` could also be used for lambda delegates: `stackalloc x => x == y`.

### Limitations

The proposed approach requires explicit allocation of a reference type known at compile-time in the scope that represents the instance lifetime. The approach does not cover other allocation patterns such as calls to factory methods.

As described, `[DoesNotEscape]` does not cover relationships between parameters and return values. For instance, the annotation for `StringBuilder StringBuilder.Append(string value)` should indicate that `this` is returned from the method.

Ideally the contract on `[DoesNotEscape] params object[] args` in `Console.WriteLine()` would describe which methods were actually used for `args[i]` to avoid a versioning issue if additional methods on `System.Object` are annotated as `DoesNotEscape` in the future.

## See also

Various proposals and issues have discussed allocating reference types on the stack, including
[coreclr/issues/1784](https://github.com/dotnet/coreclr/issues/1784), [corefxlab/pull/2595#comment](https://github.com/dotnet/corefxlab/pull/2595#discussion_r235208262).

