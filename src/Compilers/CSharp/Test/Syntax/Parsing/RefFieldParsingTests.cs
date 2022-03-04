// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class RefFieldParsingTests : ParsingTests
    {
        public RefFieldParsingTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void RefField_01()
        {
            string source = "ref T t;";
            UsingDeclaration(source, TestOptions.Regular10);
            verify();

            UsingDeclaration(source, TestOptions.RegularNext);
            verify();

            void verify()
            {
                N(SyntaxKind.FieldDeclaration);
                {
                    N(SyntaxKind.VariableDeclaration);
                    {
                        N(SyntaxKind.RefType);
                        {
                            N(SyntaxKind.RefKeyword);
                            N(SyntaxKind.IdentifierName);
                            {
                                N(SyntaxKind.IdentifierToken, "T");
                            }
                        }
                        N(SyntaxKind.VariableDeclarator);
                        {
                            N(SyntaxKind.IdentifierToken, "t");
                        }
                    }
                    N(SyntaxKind.SemicolonToken);
                }
                EOF();
            }
        }

        // PROTOTYPE: Test 'out', 'ref readonly', 'in'
    }
}
