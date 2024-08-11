// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class ValueDelegateTests : CSharpTestBase
    {
        private static string IncludeExpectedOutput(string expectedOutput) => ExecutionConditionUtil.IsMonoOrCoreClr ? expectedOutput : null;

        [Fact]
        public void Example()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                ref struct Comparer : IComparer<string>
                {
                    public int Compare(string x, string y)
                    {
                        return x.CompareTo(y);
                    }
                }
                class Program
                {
                    static int Compare<TComparer, T>(T x, T y, TComparer comparer)
                        where TComparer : IComparer<T>, allows ref struct
                    {
                        return comparer.Compare(x, y);
                    }
                    static void Main()
                    {
                        int result = Compare("Hello", "World", new Comparer());
                        Console.WriteLine(result);
                    }
                }
                """;
            CompileAndVerify(source, targetFramework: TargetFramework.Net90, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput("-1"));
        }

        // PROTOTYPE: Test capturing and modifying a ref struct local.
    }
}
