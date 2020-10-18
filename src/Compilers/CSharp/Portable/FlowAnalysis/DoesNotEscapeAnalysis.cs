// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed class DoesNotEscapeAnalysis : BoundTreeWalker
    {
        internal static void Analyze(BoundNode node, DiagnosticBag diagnostics)
        {
            var visitor = new DoesNotEscapeAnalysis(diagnostics);
            visitor.Visit(node);
        }

        private readonly ExpressionAnalysis _expressionAnalysis;
        private readonly DiagnosticBag _diagnostics;

        private DoesNotEscapeAnalysis(DiagnosticBag diagnostics)
        {
            _expressionAnalysis = new ExpressionAnalysis(diagnostics);
            _diagnostics = diagnostics;
        }

        protected override BoundExpression? VisitExpressionWithoutStackGuard(BoundExpression node)
        {
            return (BoundExpression)Visit(node);
        }

        public override BoundNode? VisitExpressionStatement(BoundExpressionStatement node)
        {
            _expressionAnalysis.Visit(node.Expression);
            return null;
        }

        public override BoundNode Visit(BoundNode node)
        {
            Debug.Assert(node is not BoundExpression);
            return base.Visit(node);
        }

        private bool VisitExpression(BoundExpression expr)
        {
            switch (expr.Kind)
            {
                case BoundKind.Call:
                    break;
                default:
                    // PROTOTYPE: Handle all expression types explicitly.
                    break;
            }
            return false;
        }

        private sealed class ExpressionAnalysis : BoundTreeVisitor<object?, bool>
        {
            private readonly DiagnosticBag _diagnostics;

            internal ExpressionAnalysis(DiagnosticBag diagnostics)
            {
                _diagnostics = diagnostics;
            }

            internal bool Visit(BoundExpression expr)
            {
                return Visit(expr, arg: null);
            }

            public override bool DefaultVisit(BoundNode node, object? arg)
            {
                // PROTOTYPE: Handle all expression types.
                return base.DefaultVisit(node, arg);
            }

            public override bool VisitConversion(BoundConversion node, object? arg)
            {
                return Visit(node.Operand);
            }

            public override bool VisitParameter(BoundParameter node, object? arg)
            {
                return node.ParameterSymbol.DoesNotEscape;
            }

            public override bool VisitCall(BoundCall node, object? arg)
            {
                var arguments = node.Arguments;
                if (arguments.Length > 0)
                {
                    var values = arguments.SelectAsArray((arg, visitor) => visitor.Visit(arg), this);
                    var parameters = node.Method.Parameters;
                    // PROTOTYPE: Match parameters correctly.
                    int n = Math.Min(arguments.Length, parameters.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (values[i] && !parameters[i].DoesNotEscape)
                        {
                            ReportEscape(arguments[i]);
                        }
                    }
                }
                return base.VisitCall(node, arg);
            }

            private void ReportEscape(BoundExpression expr)
            {
                _diagnostics.Add(ErrorCode.ERR_ReferenceMayEscape, expr.Syntax.Location);
            }
        }
    }
}
