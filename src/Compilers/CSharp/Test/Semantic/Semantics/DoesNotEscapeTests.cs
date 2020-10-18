// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Semantics
{
    public class DoesNotEscapeTests : CSharpTestBase
    {
        [Fact]
        public void ParamsArray_01()
        {
            var source =
@"using System.Runtime.CompilerServices;
class Program
{
    static void Main()
    {
        Print(""{0}, {1}"", 1, 2);
    }
    static void Print(string format, [DoesNotEscape] params object[] args)
    {
    }
}";
            var verifier = CompileAndVerify(new[] { source, DoesNotEscapeAttributeDefinition });
            // PROTOTYPE: How is 'newarr' marked as stack allocated? Perhaps a new IL prefix that is dropped when emitting.
            verifier.VerifyIL("Program.Main",
@"{
  // Code size       35 (0x23)
  .maxstack  5
  IL_0000:  ldstr      ""{0}, {1}""
  IL_0005:  ldc.i4.2
  IL_0006:  newarr     ""object""
  IL_000b:  dup
  IL_000c:  ldc.i4.0
  IL_000d:  ldc.i4.1
  IL_000e:  box        ""int""
  IL_0013:  stelem.ref
  IL_0014:  dup
  IL_0015:  ldc.i4.1
  IL_0016:  ldc.i4.2
  IL_0017:  box        ""int""
  IL_001c:  stelem.ref
  IL_001d:  call       ""void Program.Print(string, params object[])""
  IL_0022:  ret
}");
        }

        [Fact]
        public void ParamsArray_02()
        {
            var source =
@"using System.Runtime.CompilerServices;
class Program
{
    static void Print(string format, [DoesNotEscape] params object[] args)
    {
        Other(args);
    }
    static void Other(object obj)
    {
    }
}";
            var comp = CreateCompilation(new[] { source, DoesNotEscapeAttributeDefinition });
            comp.VerifyDiagnostics(
                // (6,15): error CS8717: Reference may escape.
                //         Other(args);
                Diagnostic(ErrorCode.ERR_ReferenceMayEscape, "args").WithLocation(6, 15));
        }
    }
}
