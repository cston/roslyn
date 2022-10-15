// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed class RefSafetyAnalysis : BoundTreeVisitor<RefSafetyAnalysis.Arg, uint>
    {
        private const uint UnusedEscapeScope = uint.MaxValue;

        internal readonly struct Arg
        {
            public readonly uint LocalScopeDepth;
            public readonly bool IsRefEscape;
            public readonly uint EscapeTo;

            public Arg(uint localScopeDepth, bool isRefEscape, uint escapeTo)
            {
                LocalScopeDepth = localScopeDepth;
                IsRefEscape = isRefEscape;
                EscapeTo = escapeTo;
            }

            public Arg WithEscapeTo(bool isRefEscape, uint escapeTo) => new Arg(LocalScopeDepth, isRefEscape, escapeTo);
        }

        internal readonly struct EscapeScopes
        {
            public readonly uint RefScope;
            public readonly uint ValScope;

            public EscapeScopes(uint refScope, uint valScope)
            {
                RefScope = refScope;
                ValScope = valScope;
            }
        }

        private readonly CSharpCompilation _compilation;
        private readonly MethodSymbol _method;
        private readonly bool _inUnsafeRegion;
        private readonly bool _useUpdatedEscapeRules;
        private readonly BindingDiagnosticBag _diagnostics;
        private readonly Dictionary<LocalSymbol, EscapeScopes> _localEscapeScopes;

        public static void Analyze(CSharpCompilation compilation, MethodSymbol method, BoundNode node, BindingDiagnosticBag diagnostics)
        {
            var visitor = new RefSafetyAnalysis(
                compilation,
                method,
                inUnsafeRegion: InUnsafeRegion(method),
                useUpdatedEscapeRules: method.ContainingModule.UseUpdatedEscapeRules,
                diagnostics,
                new Dictionary<LocalSymbol, EscapeScopes>());
            visitor.Visit(node, new Arg(localScopeDepth: 2, isRefEscape: false, escapeTo: UnusedEscapeScope));
        }

        // PROTOTYPE: Improve this helper method.
        private static bool InUnsafeRegion(MethodSymbol method)
        {
            if (method is SourceMemberMethodSymbol { IsUnsafe: true })
            {
                return true;
            }
            return InUnsafeRegion(method.ContainingType);
        }

        private static bool InUnsafeRegion(NamedTypeSymbol type)
        {
            while (type is { })
            {
                var def = type.OriginalDefinition;
                if (def is SourceMemberContainerTypeSymbol { IsUnsafe: true })
                {
                    return true;
                }
                type = def.ContainingType;
            }
            return false;
        }

        private RefSafetyAnalysis(
            CSharpCompilation compilation,
            MethodSymbol method,
            bool inUnsafeRegion,
            bool useUpdatedEscapeRules,
            BindingDiagnosticBag diagnostics,
            Dictionary<LocalSymbol, EscapeScopes> localEscapeScopes)
        {
            _compilation = compilation;
            _method = method;
            _inUnsafeRegion = inUnsafeRegion;
            _useUpdatedEscapeRules = useUpdatedEscapeRules;
            _diagnostics = diagnostics;
            _localEscapeScopes = localEscapeScopes;
        }

        internal RefSafetyAnalysis WithUnsafeRegion()
        {
            return _inUnsafeRegion ?
                this :
                new RefSafetyAnalysis(_compilation, _method, inUnsafeRegion: true, useUpdatedEscapeRules: _useUpdatedEscapeRules, _diagnostics, _localEscapeScopes);
        }

        public override uint DefaultVisit(BoundNode node, Arg arg)
        {
            throw ExceptionUtilities.UnexpectedValue(node);
        }

        private void VisitList<T>(ImmutableArray<T> list, Arg arg)
            where T : BoundNode
        {
            if (list.IsDefault)
            {
                return;
            }
            foreach (var item in list)
            {
                Visit(item, arg);
            }
        }

        private uint VisitExpressions(ImmutableArray<BoundExpression> expressions, Arg arg)
        {
            var result = Binder.CallingMethodScope;
            foreach (var expression in expressions)
            {
                result = Math.Max(result, Visit(expression, arg));
            }
            return result;
        }

        public override uint VisitFieldEqualsValue(BoundFieldEqualsValue node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitPropertyEqualsValue(BoundPropertyEqualsValue node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitParameterEqualsValue(BoundParameterEqualsValue node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitGlobalStatementInitializer(BoundGlobalStatementInitializer node, Arg arg)
        {
            Visit(node.Statement, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitValuePlaceholder(BoundValuePlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitDeconstructValuePlaceholder(BoundDeconstructValuePlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitTupleOperandPlaceholder(BoundTupleOperandPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitAwaitableValuePlaceholder(BoundAwaitableValuePlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitDisposableValuePlaceholder(BoundDisposableValuePlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();

        public override uint VisitObjectOrCollectionValuePlaceholder(BoundObjectOrCollectionValuePlaceholder node, Arg arg)
        {
            // binder uses this as a placeholder when binding members inside an object initializer
            // just say it does not escape anywhere, so that we do not get false errors.
            return arg.LocalScopeDepth;
        }

        public override uint VisitImplicitIndexerValuePlaceholder(BoundImplicitIndexerValuePlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitImplicitIndexerReceiverPlaceholder(BoundImplicitIndexerReceiverPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitListPatternReceiverPlaceholder(BoundListPatternReceiverPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitListPatternIndexPlaceholder(BoundListPatternIndexPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitSlicePatternReceiverPlaceholder(BoundSlicePatternReceiverPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitSlicePatternRangePlaceholder(BoundSlicePatternRangePlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitDup(BoundDup node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitPassByCopy(BoundPassByCopy node, Arg arg)
        {
            Visit(node.Expression, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitBadExpression(BoundBadExpression node, Arg arg)
        {
            VisitList(node.ChildBoundNodes, arg);
            return Binder.CallingMethodScope;
        }
        public override uint VisitBadStatement(BoundBadStatement node, Arg arg)
        {
            VisitList(node.ChildBoundNodes, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitExtractedFinallyBlock(BoundExtractedFinallyBlock node, Arg arg)
        {
            Visit(node.FinallyBlock, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitTypeExpression(BoundTypeExpression node, Arg arg)
        {
            Visit(node.BoundContainingTypeOpt, arg);
            VisitList(node.BoundDimensionsOpt, arg);
            return Binder.CallingMethodScope;
        }
        public override uint VisitTypeOrValueExpression(BoundTypeOrValueExpression node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitNamespaceExpression(BoundNamespaceExpression node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitUnaryOperator(BoundUnaryOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitIncrementOperator(BoundIncrementOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitAddressOfOperator(BoundAddressOfOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitUnconvertedAddressOfOperator(BoundUnconvertedAddressOfOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitFunctionPointerLoad(BoundFunctionPointerLoad node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitPointerIndirectionOperator(BoundPointerIndirectionOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitPointerElementAccess(BoundPointerElementAccess node, Arg arg)
        {
            Visit(node.Expression, arg);
            Visit(node.Index, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitFunctionPointerInvocation(BoundFunctionPointerInvocation node, Arg arg)
        {
            Visit(node.InvokedExpression, arg);
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitRefTypeOperator(BoundRefTypeOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitMakeRefOperator(BoundMakeRefOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitRefValueOperator(BoundRefValueOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            // The undocumented __refvalue(tr, T) expression results in an lvalue of type T.
            // for compat reasons it is not ref-returnable (since TypedReference is not val-returnable)
            if (arg.EscapeTo is Binder.CallingMethodScope or Binder.ReturnOnlyScope)
            {
                ReportStandardRValueRefEscapeError(node.Syntax, arg.EscapeTo);
            }
            return Binder.CallingMethodScope;
        }

        private void ReportStandardRValueRefEscapeError(SyntaxNode node, uint escapeTo)
        {
            Error(_diagnostics, Binder.GetStandardRValueRefEscapeError(escapeTo), node);
        }

        public override uint VisitFromEndIndexExpression(BoundFromEndIndexExpression node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitRangeExpression(BoundRangeExpression node, Arg arg)
        {
            Visit(node.LeftOperandOpt, arg);
            Visit(node.RightOperandOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitBinaryOperator(BoundBinaryOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitTupleBinaryOperator(BoundTupleBinaryOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitUserDefinedConditionalLogicalOperator(BoundUserDefinedConditionalLogicalOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitCompoundAssignmentOperator(BoundCompoundAssignmentOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitAssignmentOperator(BoundAssignmentOperator node, Arg arg)
        {
            var op1 = node.Left;
            if (op1.HasAnyErrors)
            {
                return Binder.CallingMethodScope;
            }

            var op2 = node.Right;

            if (node.IsRef)
            {
                arg = arg.WithEscapeTo(isRefEscape: true, escapeTo: arg.LocalScopeDepth);
                var leftEscape = Visit(op1, arg);
                var rightEscape = Visit(op2, arg);

                if (leftEscape < rightEscape)
                {
                    var errorCode = (rightEscape, _inUnsafeRegion) switch
                    {
                        (Binder.ReturnOnlyScope, false) => ErrorCode.ERR_RefAssignReturnOnly,
                        (Binder.ReturnOnlyScope, true) => ErrorCode.WRN_RefAssignReturnOnly,
                        (_, false) => ErrorCode.ERR_RefAssignNarrower,
                        (_, true) => ErrorCode.WRN_RefAssignNarrower
                    };

                    Error(_diagnostics, errorCode, node.Syntax, getName(op1), op2.Syntax);
                }
                else if (op1.Kind is BoundKind.Local or BoundKind.Parameter)
                {
                    // PROTOTYPE: Add the else if block from Binder.BindAssignment().
                }

                return leftEscape;
            }
            else
            {
                var leftEscape = Visit(op1, arg.WithEscapeTo(isRefEscape: false, arg.LocalScopeDepth));
                _ = Visit(op2, arg.WithEscapeTo(isRefEscape: false, escapeTo: leftEscape));
                return leftEscape;
            }

            static object getName(BoundExpression expr)
            {
                if (expr.ExpressionSymbol is { Name: var name })
                {
                    return name;
                }
                if (expr is BoundArrayAccess)
                {
                    return MessageID.IDS_ArrayAccess.Localize();
                }
                if (expr is BoundPointerElementAccess)
                {
                    return MessageID.IDS_PointerElementAccess.Localize();
                }

                Debug.Assert(false);
                return "";
            }
        }
        public override uint VisitDeconstructionAssignmentOperator(BoundDeconstructionAssignmentOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitNullCoalescingOperator(BoundNullCoalescingOperator node, Arg arg)
        {
            Visit(node.LeftOperand, arg);
            Visit(node.RightOperand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitNullCoalescingAssignmentOperator(BoundNullCoalescingAssignmentOperator node, Arg arg)
        {
            Visit(node.LeftOperand, arg);
            Visit(node.RightOperand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitUnconvertedConditionalOperator(BoundUnconvertedConditionalOperator node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Consequence, arg);
            Visit(node.Alternative, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitConditionalOperator(BoundConditionalOperator node, Arg arg)
        {
            Visit(node.Condition, arg);
            var consequenceResult = Visit(node.Consequence, arg);
            var alternativeResult = Visit(node.Alternative, arg);
            return Math.Max(consequenceResult, alternativeResult);
        }

        public override uint VisitArrayAccess(BoundArrayAccess node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.Indices, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitArrayLength(BoundArrayLength node, Arg arg)
        {
            Visit(node.Expression, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitAwaitableInfo(BoundAwaitableInfo node, Arg arg)
        {
            Visit(node.AwaitableInstancePlaceholder, arg);
            Visit(node.GetAwaiter, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitAwaitExpression(BoundAwaitExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            Visit(node.AwaitableInfo, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitTypeOfOperator(BoundTypeOfOperator node, Arg arg)
        {
            Visit(node.SourceType, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitMethodDefIndex(BoundMethodDefIndex node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitMaximumMethodDefIndex(BoundMaximumMethodDefIndex node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitInstrumentationPayloadRoot(BoundInstrumentationPayloadRoot node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitModuleVersionId(BoundModuleVersionId node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitModuleVersionIdString(BoundModuleVersionIdString node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitSourceDocumentIndex(BoundSourceDocumentIndex node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitMethodInfo(BoundMethodInfo node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitFieldInfo(BoundFieldInfo node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitDefaultLiteral(BoundDefaultLiteral node, Arg arg) => throw ExceptionUtilities.Unreachable();

        public override uint VisitDefaultExpression(BoundDefaultExpression node, Arg arg) => Binder.CallingMethodScope;

        public override uint VisitIsOperator(BoundIsOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            Visit(node.TargetType, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitAsOperator(BoundAsOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            Visit(node.TargetType, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitSizeOfOperator(BoundSizeOfOperator node, Arg arg)
        {
            Visit(node.SourceType, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitConversion(BoundConversion node, Arg arg)
        {
            return Visit(node.Operand, arg);
        }

        public override uint VisitReadOnlySpanFromArray(BoundReadOnlySpanFromArray node, Arg arg)
        {
            Visit(node.Operand, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitArgList(BoundArgList node, Arg arg) => Binder.CallingMethodScope;
        public override uint VisitArgListOperator(BoundArgListOperator node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitFixedLocalCollectionInitializer(BoundFixedLocalCollectionInitializer node, Arg arg)
        {
            Visit(node.Expression, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSequencePoint(BoundSequencePoint node, Arg arg)
        {
            Visit(node.StatementOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSequencePointWithSpan(BoundSequencePointWithSpan node, Arg arg)
        {
            Visit(node.StatementOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSavePreviousSequencePoint(BoundSavePreviousSequencePoint node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitRestorePreviousSequencePoint(BoundRestorePreviousSequencePoint node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitStepThroughSequencePoint(BoundStepThroughSequencePoint node, Arg arg) => throw ExceptionUtilities.Unreachable();

        public override uint VisitBlock(BoundBlock node, Arg arg)
        {
            VisitList(node.Statements, new Arg(arg.LocalScopeDepth + 1, isRefEscape: false, escapeTo: UnusedEscapeScope));
            return UnusedEscapeScope;
        }

        public override uint VisitScope(BoundScope node, Arg arg)
        {
            VisitList(node.Statements, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitStateMachineScope(BoundStateMachineScope node, Arg arg)
        {
            Visit(node.Statement, arg);
            return UnusedEscapeScope;
        }

        public override uint VisitLocalDeclaration(BoundLocalDeclaration node, Arg arg)
        {
            Visit(node.DeclaredTypeOpt, arg);
            var result = Visit(node.InitializerOpt, arg);
            _localEscapeScopes.Add(node.LocalSymbol, new EscapeScopes(refScope: arg.LocalScopeDepth, valScope: result));
            VisitList(node.ArgumentsOpt, arg);
            return UnusedEscapeScope;
        }

        public override uint VisitMultipleLocalDeclarations(BoundMultipleLocalDeclarations node, Arg arg)
        {
            VisitList(node.LocalDeclarations, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitUsingLocalDeclarations(BoundUsingLocalDeclarations node, Arg arg)
        {
            Visit(node.AwaitOpt, arg);
            VisitList(node.LocalDeclarations, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitLocalFunctionStatement(BoundLocalFunctionStatement node, Arg arg)
        {
            Visit(node.BlockBody, arg);
            Visit(node.ExpressionBody, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitNoOpStatement(BoundNoOpStatement node, Arg arg) => UnusedEscapeScope;

        public override uint VisitReturnStatement(BoundReturnStatement node, Arg arg)
        {
            Visit(
                node.ExpressionOpt,
                arg.WithEscapeTo(isRefEscape: node.RefKind != RefKind.None, escapeTo: Binder.ReturnOnlyScope));
            return UnusedEscapeScope;
        }

        public override uint VisitYieldReturnStatement(BoundYieldReturnStatement node, Arg arg)
        {
            // PROTOTYPE: Should set arg.WithEscapeTo(...) similar to VisitReturnStatement() above.
            Visit(node.Expression, arg);
            return UnusedEscapeScope;
        }

        public override uint VisitYieldBreakStatement(BoundYieldBreakStatement node, Arg arg) => UnusedEscapeScope;
        public override uint VisitThrowStatement(BoundThrowStatement node, Arg arg)
        {
            Visit(node.ExpressionOpt, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitExpressionStatement(BoundExpressionStatement node, Arg arg)
        {
            Visit(node.Expression, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitBreakStatement(BoundBreakStatement node, Arg arg) => UnusedEscapeScope;
        public override uint VisitContinueStatement(BoundContinueStatement node, Arg arg) => UnusedEscapeScope;
        public override uint VisitSwitchStatement(BoundSwitchStatement node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.SwitchSections, arg);
            Visit(node.DefaultLabel, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitSwitchDispatch(BoundSwitchDispatch node, Arg arg)
        {
            Visit(node.Expression, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitIfStatement(BoundIfStatement node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Consequence, arg);
            Visit(node.AlternativeOpt, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitDoStatement(BoundDoStatement node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitWhileStatement(BoundWhileStatement node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitForStatement(BoundForStatement node, Arg arg)
        {
            Visit(node.Initializer, arg);
            Visit(node.Condition, arg);
            Visit(node.Increment, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitForEachStatement(BoundForEachStatement node, Arg arg)
        {
            Visit(node.IterationVariableType, arg);
            Visit(node.IterationErrorExpressionOpt, arg);
            Visit(node.Expression, arg);
            Visit(node.DeconstructionOpt, arg);
            Visit(node.AwaitOpt, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitForEachDeconstructStep(BoundForEachDeconstructStep node, Arg arg)
        {
            Visit(node.DeconstructionAssignment, arg);
            Visit(node.TargetPlaceholder, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitUsingStatement(BoundUsingStatement node, Arg arg)
        {
            Visit(node.DeclarationsOpt, arg);
            Visit(node.ExpressionOpt, arg);
            Visit(node.Body, arg);
            Visit(node.AwaitOpt, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitFixedStatement(BoundFixedStatement node, Arg arg)
        {
            Visit(node.Declarations, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitLockStatement(BoundLockStatement node, Arg arg)
        {
            Visit(node.Argument, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitTryStatement(BoundTryStatement node, Arg arg)
        {
            Visit(node.TryBlock, arg);
            VisitList(node.CatchBlocks, arg);
            Visit(node.FinallyBlockOpt, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitCatchBlock(BoundCatchBlock node, Arg arg)
        {
            Visit(node.ExceptionSourceOpt, arg);
            Visit(node.ExceptionFilterPrologueOpt, arg);
            Visit(node.ExceptionFilterOpt, arg);
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }

        public override uint VisitLiteral(BoundLiteral node, Arg arg) => Binder.CallingMethodScope;
        public override uint VisitUtf8String(BoundUtf8String node, Arg arg) => Binder.CallingMethodScope;

        public override uint VisitThisReference(BoundThisReference node, Arg arg)
        {
            var thisParam = _method.ThisParameter;
            if (thisParam is null)
            {
                return Binder.CallingMethodScope;
            }
            Debug.Assert(thisParam.Type.Equals(node.Type, TypeCompareKind.ConsiderEverything));
            return VisitParameter(node, thisParam, arg);
        }

        public override uint VisitPreviousSubmissionReference(BoundPreviousSubmissionReference node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitHostObjectMemberReference(BoundHostObjectMemberReference node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitBaseReference(BoundBaseReference node, Arg arg) => throw ExceptionUtilities.Unreachable();

        public override uint VisitLocal(BoundLocal node, Arg arg)
        {
            var localSymbol = node.LocalSymbol;
            var localScopes = _localEscapeScopes[localSymbol];
            uint result = arg.IsRefEscape ? localScopes.RefScope : localScopes.ValScope;
            if (result > arg.EscapeTo)
            {
                var syntax = node.Syntax;
                if (arg.IsRefEscape)
                {
                    if (arg.EscapeTo is Binder.CallingMethodScope or Binder.ReturnOnlyScope)
                    {
                        bool checkingReceiver = false; // PROTOTYPE:
                        if (localSymbol.RefKind == RefKind.None)
                        {
                            if (checkingReceiver)
                            {
                                Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_RefReturnLocal2 : ErrorCode.ERR_RefReturnLocal2, syntax, localSymbol);
                            }
                            else
                            {
                                Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_RefReturnLocal : ErrorCode.ERR_RefReturnLocal, syntax, localSymbol);
                            }
                        }
                        else if (checkingReceiver)
                        {
                            Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_RefReturnNonreturnableLocal2 : ErrorCode.ERR_RefReturnNonreturnableLocal2, syntax, localSymbol);
                        }
                        else
                        {
                            Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_RefReturnNonreturnableLocal : ErrorCode.ERR_RefReturnNonreturnableLocal, syntax, localSymbol);
                        }
                    }
                    else
                    {
                        Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_EscapeVariable : ErrorCode.ERR_EscapeVariable, syntax, localSymbol);
                    }
                }
                else
                {
                    Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_EscapeVariable : ErrorCode.ERR_EscapeVariable, syntax, localSymbol);
                }
            }
            return result;
        }

        public override uint VisitPseudoVariable(BoundPseudoVariable node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitRangeVariable(BoundRangeVariable node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitParameter(BoundParameter node, Arg arg)
        {
            return VisitParameter(node, node.ParameterSymbol, arg);
        }

        private uint VisitParameter(BoundExpression node, ParameterSymbol parameter, Arg arg)
        {
            uint escapeTo = arg.EscapeTo;
            return arg.IsRefEscape ?
                CheckParameterRefEscape(node.Syntax, node, parameter, escapeTo, checkingReceiver: false /*PROTOTYPE:*/) :
                CheckParameterValEscape(node.Syntax, parameter, escapeTo);
        }

        private uint CheckParameterValEscape(SyntaxNode node, ParameterSymbol parameter, uint escapeTo)
        {
            var result = parameter switch
            {
                { EffectiveScope: DeclarationScope.ValueScoped } => Binder.CurrentMethodScope,
                { RefKind: RefKind.Out, UseUpdatedEscapeRules: true } => Binder.ReturnOnlyScope,
                _ => Binder.CallingMethodScope
            };
            if (result > escapeTo)
            {
                Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_EscapeVariable : ErrorCode.ERR_EscapeVariable, node, parameter);
            }
            return result;
        }

        private uint CheckParameterRefEscape(SyntaxNode node, BoundExpression parameter, ParameterSymbol parameterSymbol, uint escapeTo, bool checkingReceiver)
        {
            var refSafeToEscape = parameterSymbol switch
            {
                { RefKind: RefKind.None } => Binder.CurrentMethodScope,
                { EffectiveScope: DeclarationScope.RefScoped } => Binder.CurrentMethodScope,
                _ => Binder.ReturnOnlyScope
            };

            if (refSafeToEscape > escapeTo)
            {
                var isRefScoped = parameterSymbol.EffectiveScope == DeclarationScope.RefScoped;
                Debug.Assert(parameterSymbol.RefKind == RefKind.None || isRefScoped || refSafeToEscape == Binder.ReturnOnlyScope);

                if (parameter is BoundThisReference)
                {
                    Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_RefReturnStructThis : ErrorCode.ERR_RefReturnStructThis, node);
                }
                else
                {
#pragma warning disable format
                    bool inUnsafeRegion = _inUnsafeRegion;
                    var (errorCode, syntax) = (checkingReceiver, isRefScoped, inUnsafeRegion, refSafeToEscape) switch
                    {
                        (checkingReceiver: true,  isRefScoped: true,  inUnsafeRegion: false, _)                      => (ErrorCode.ERR_RefReturnScopedParameter2, parameter.Syntax),
                        (checkingReceiver: true,  isRefScoped: true,  inUnsafeRegion: true,  _)                      => (ErrorCode.WRN_RefReturnScopedParameter2, parameter.Syntax),
                        (checkingReceiver: true,  isRefScoped: false, inUnsafeRegion: false, Binder.ReturnOnlyScope) => (ErrorCode.ERR_RefReturnOnlyParameter2,   parameter.Syntax),
                        (checkingReceiver: true,  isRefScoped: false, inUnsafeRegion: true,  Binder.ReturnOnlyScope) => (ErrorCode.WRN_RefReturnOnlyParameter2,   parameter.Syntax),
                        (checkingReceiver: true,  isRefScoped: false, inUnsafeRegion: false, _)                      => (ErrorCode.ERR_RefReturnParameter2,       parameter.Syntax),
                        (checkingReceiver: true,  isRefScoped: false, inUnsafeRegion: true,  _)                      => (ErrorCode.WRN_RefReturnParameter2,       parameter.Syntax),
                        (checkingReceiver: false, isRefScoped: true,  inUnsafeRegion: false, _)                      => (ErrorCode.ERR_RefReturnScopedParameter,  node),
                        (checkingReceiver: false, isRefScoped: true,  inUnsafeRegion: true,  _)                      => (ErrorCode.WRN_RefReturnScopedParameter,  node),
                        (checkingReceiver: false, isRefScoped: false, inUnsafeRegion: false, Binder.ReturnOnlyScope) => (ErrorCode.ERR_RefReturnOnlyParameter,    node),
                        (checkingReceiver: false, isRefScoped: false, inUnsafeRegion: true,  Binder.ReturnOnlyScope) => (ErrorCode.WRN_RefReturnOnlyParameter,    node),
                        (checkingReceiver: false, isRefScoped: false, inUnsafeRegion: false, _)                      => (ErrorCode.ERR_RefReturnParameter,        node),
                        (checkingReceiver: false, isRefScoped: false, inUnsafeRegion: true,  _)                      => (ErrorCode.WRN_RefReturnParameter,        node)
                    };
#pragma warning restore format
                    Error(_diagnostics, errorCode, syntax, parameterSymbol.Name);
                }
            }

            return refSafeToEscape;
        }

        public override uint VisitLabelStatement(BoundLabelStatement node, Arg arg) => UnusedEscapeScope;
        public override uint VisitGotoStatement(BoundGotoStatement node, Arg arg)
        {
            Visit(node.CaseExpressionOpt, arg);
            Visit(node.LabelExpressionOpt, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitLabeledStatement(BoundLabeledStatement node, Arg arg)
        {
            Visit(node.Body, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitLabel(BoundLabel node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitStatementList(BoundStatementList node, Arg arg)
        {
            VisitList(node.Statements, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitConditionalGoto(BoundConditionalGoto node, Arg arg)
        {
            Visit(node.Condition, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSwitchExpressionArm(BoundSwitchExpressionArm node, Arg arg)
        {
            Visit(node.Pattern, arg);
            Visit(node.WhenClause, arg);
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitUnconvertedSwitchExpression(BoundUnconvertedSwitchExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.SwitchArms, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitConvertedSwitchExpression(BoundConvertedSwitchExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.SwitchArms, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDecisionDag(BoundDecisionDag node, Arg arg)
        {
            Visit(node.RootNode, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitEvaluationDecisionDagNode(BoundEvaluationDecisionDagNode node, Arg arg)
        {
            Visit(node.Evaluation, arg);
            Visit(node.Next, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitTestDecisionDagNode(BoundTestDecisionDagNode node, Arg arg)
        {
            Visit(node.Test, arg);
            Visit(node.WhenTrue, arg);
            Visit(node.WhenFalse, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitWhenDecisionDagNode(BoundWhenDecisionDagNode node, Arg arg)
        {
            Visit(node.WhenExpression, arg);
            Visit(node.WhenTrue, arg);
            Visit(node.WhenFalse, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitLeafDecisionDagNode(BoundLeafDecisionDagNode node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitDagTemp(BoundDagTemp node, Arg arg)
        {
            Visit(node.Source, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagTypeTest(BoundDagTypeTest node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagNonNullTest(BoundDagNonNullTest node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagExplicitNullTest(BoundDagExplicitNullTest node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagValueTest(BoundDagValueTest node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagRelationalTest(BoundDagRelationalTest node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagDeconstructEvaluation(BoundDagDeconstructEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagTypeEvaluation(BoundDagTypeEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagFieldEvaluation(BoundDagFieldEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagPropertyEvaluation(BoundDagPropertyEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagIndexEvaluation(BoundDagIndexEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagIndexerEvaluation(BoundDagIndexerEvaluation node, Arg arg)
        {
            Visit(node.LengthTemp, arg);
            Visit(node.IndexerAccess, arg);
            Visit(node.ReceiverPlaceholder, arg);
            Visit(node.ArgumentPlaceholder, arg);
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagSliceEvaluation(BoundDagSliceEvaluation node, Arg arg)
        {
            Visit(node.LengthTemp, arg);
            Visit(node.IndexerAccess, arg);
            Visit(node.ReceiverPlaceholder, arg);
            Visit(node.ArgumentPlaceholder, arg);
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDagAssignmentEvaluation(BoundDagAssignmentEvaluation node, Arg arg)
        {
            Visit(node.Target, arg);
            Visit(node.Input, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSwitchSection(BoundSwitchSection node, Arg arg)
        {
            VisitList(node.SwitchLabels, arg);
            VisitList(node.Statements, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSwitchLabel(BoundSwitchLabel node, Arg arg)
        {
            Visit(node.Pattern, arg);
            Visit(node.WhenClause, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSequencePointExpression(BoundSequencePointExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSequence(BoundSequence node, Arg arg)
        {
            VisitList(node.SideEffects, arg);
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSpillSequence(BoundSpillSequence node, Arg arg)
        {
            VisitList(node.SideEffects, arg);
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDynamicMemberAccess(BoundDynamicMemberAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDynamicInvocation(BoundDynamicInvocation node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitConditionalAccess(BoundConditionalAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.AccessExpression, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitLoweredConditionalAccess(BoundLoweredConditionalAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.WhenNotNull, arg);
            Visit(node.WhenNullOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitConditionalReceiver(BoundConditionalReceiver node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitComplexConditionalReceiver(BoundComplexConditionalReceiver node, Arg arg)
        {
            Visit(node.ValueTypeReceiver, arg);
            Visit(node.ReferenceTypeReceiver, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitMethodGroup(BoundMethodGroup node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitPropertyGroup(BoundPropertyGroup node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitCall(BoundCall node, Arg arg)
        {
            var method = node.Method;
            return VisitCall(
                node.Syntax,
                method,
                node.ReceiverOpt,
                method.Parameters,
                node.Arguments,
                node.ArgumentRefKindsOpt,
                node.ArgsToParamsOpt,
                arg);
        }

        public override uint VisitEventAssignmentOperator(BoundEventAssignmentOperator node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            Visit(node.Argument, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitAttribute(BoundAttribute node, Arg arg)
        {
            VisitList(node.ConstructorArguments, arg);
            VisitList(node.NamedArguments, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitUnconvertedObjectCreationExpression(BoundUnconvertedObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitObjectCreationExpression(BoundObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            var initializerResult = Visit(node.InitializerExpressionOpt, arg);

            var constructorSymbol = node.Constructor;
            var result = VisitCall(
                node.Syntax,
                constructorSymbol,
                null,
                constructorSymbol.Parameters,
                node.Arguments,
                node.ArgumentRefKindsOpt,
                node.ArgsToParamsOpt,
                arg);

            return Math.Max(result, initializerResult);
        }

        public override uint VisitTupleLiteral(BoundTupleLiteral node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitConvertedTupleLiteral(BoundConvertedTupleLiteral node, Arg arg)
        {
            return CheckTupleValEscape(node.Arguments, arg);
        }

        private uint CheckTupleValEscape(ImmutableArray<BoundExpression> elements, Arg arg)
        {
            uint narrowestScope = arg.LocalScopeDepth;
            foreach (var element in elements)
            {
                narrowestScope = Math.Max(narrowestScope, Visit(element, arg));
            }
            return narrowestScope;
        }

        public override uint VisitDynamicObjectCreationExpression(BoundDynamicObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            Visit(node.InitializerExpressionOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitNoPiaObjectCreationExpression(BoundNoPiaObjectCreationExpression node, Arg arg)
        {
            Visit(node.InitializerExpressionOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitObjectInitializerExpression(BoundObjectInitializerExpression node, Arg arg)
        {
            return CheckValEscapeOfObjectInitializer(node, arg);
        }

        private uint CheckValEscapeOfObjectInitializer(BoundObjectInitializerExpression initExpr, Arg arg)
        {
            uint escapeTo = arg.EscapeTo;
            var result = Binder.CallingMethodScope;
            foreach (var expression in initExpr.Initializers)
            {
                if (expression.Kind == BoundKind.AssignmentOperator)
                {
                    var assignment = (BoundAssignmentOperator)expression;
                    var rightEscape = Visit(assignment.Right, arg.WithEscapeTo(isRefEscape: assignment.IsRef, arg.EscapeTo));
                    result = Math.Max(result, rightEscape);

                    if (assignment.Left is BoundObjectInitializerMember left)
                    {
                        result = Math.Max(result, VisitExpressions(left.Arguments, arg));
                    }
                }
                else
                {
                    result = Math.Max(result, Visit(expression, arg));
                }
            }
            return result;
        }

        public override uint VisitObjectInitializerMember(BoundObjectInitializerMember node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDynamicObjectInitializerMember(BoundDynamicObjectInitializerMember node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitCollectionInitializerExpression(BoundCollectionInitializerExpression node, Arg arg)
        {
            Visit(node.Placeholder, arg);
            VisitList(node.Initializers, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitCollectionElementInitializer(BoundCollectionElementInitializer node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            Visit(node.ImplicitReceiverOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDynamicCollectionElementInitializer(BoundDynamicCollectionElementInitializer node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitImplicitReceiver(BoundImplicitReceiver node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitAnonymousObjectCreationExpression(BoundAnonymousObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            VisitList(node.Declarations, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitAnonymousPropertyDeclaration(BoundAnonymousPropertyDeclaration node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitNewT(BoundNewT node, Arg arg)
        {
            Visit(node.InitializerExpressionOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitDelegateCreationExpression(BoundDelegateCreationExpression node, Arg arg)
        {
            Visit(node.Argument, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitArrayCreation(BoundArrayCreation node, Arg arg)
        {
            VisitList(node.Bounds, arg);
            Visit(node.InitializerOpt, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitArrayInitialization(BoundArrayInitialization node, Arg arg)
        {
            VisitList(node.Initializers, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitStackAllocArrayCreation(BoundStackAllocArrayCreation node, Arg arg)
        {
            return VisitStackAllocArrayCreationBase(node, arg);
        }

        public override uint VisitConvertedStackAllocExpression(BoundConvertedStackAllocExpression node, Arg arg)
        {
            return VisitStackAllocArrayCreationBase(node, arg);
        }

        private uint VisitStackAllocArrayCreationBase(BoundStackAllocArrayCreationBase node, Arg arg)
        {
            Visit(node.Count, arg);
            Visit(node.InitializerOpt, arg);

            if (arg.EscapeTo < Binder.CurrentMethodScope)
            {
                Error(_diagnostics, _inUnsafeRegion ? ErrorCode.WRN_EscapeStackAlloc : ErrorCode.ERR_EscapeStackAlloc, node.Syntax, node.Type);
            }

            return Binder.CurrentMethodScope;
        }

        public override uint VisitFieldAccess(BoundFieldAccess node, Arg arg)
        {
            var fieldSymbol = node.FieldSymbol;

            // SPEC: If `F` is a `ref` field its ref-safe-to-escape scope is the safe-to-escape scope of `e`.
            if (_useUpdatedEscapeRules &&
                arg.IsRefEscape &&
                fieldSymbol.RefKind != RefKind.None)
            {
                if (fieldSymbol.RefKind != RefKind.None)
                {
                    return Visit(node.ReceiverOpt, arg.WithEscapeTo(isRefEscape: false, arg.EscapeTo));
                }
            }

            var receiverResult = Visit(node.ReceiverOpt, arg);

            // fields that are static or belong to reference types can ref escape anywhere
            if (fieldSymbol.IsStatic || fieldSymbol.ContainingType.IsReferenceType)
            {
                return Binder.CallingMethodScope;
            }

            if (!arg.IsRefEscape &&
                !fieldSymbol.ContainingType.IsRefLikeType)
            {
                return Binder.CallingMethodScope;
            }

            // for other fields defer to the receiver.
            return receiverResult;
        }

        public override uint VisitHoistedFieldAccess(BoundHoistedFieldAccess node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitPropertyAccess(BoundPropertyAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitEventAccess(BoundEventAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitIndexerAccess(BoundIndexerAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitImplicitIndexerAccess(BoundImplicitIndexerAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.Argument, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDynamicIndexerAccess(BoundDynamicIndexerAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            VisitList(node.Arguments, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitLambda(BoundLambda node, Arg arg)
        {
            Visit(node.Body, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitUnboundLambda(UnboundLambda node, Arg arg) => Binder.CallingMethodScope;

        public override uint VisitQueryClause(BoundQueryClause node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitTypeOrInstanceInitializers(BoundTypeOrInstanceInitializers node, Arg arg)
        {
            VisitList(node.Statements, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitNameOfOperator(BoundNameOfOperator node, Arg arg)
        {
            Visit(node.Argument, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitUnconvertedInterpolatedString(BoundUnconvertedInterpolatedString node, Arg arg)
        {
            VisitList(node.Parts, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitInterpolatedString(BoundInterpolatedString node, Arg arg)
        {
            VisitList(node.Parts, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitInterpolatedStringHandlerPlaceholder(BoundInterpolatedStringHandlerPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitInterpolatedStringArgumentPlaceholder(BoundInterpolatedStringArgumentPlaceholder node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitStringInsert(BoundStringInsert node, Arg arg)
        {
            Visit(node.Value, arg);
            Visit(node.Alignment, arg);
            Visit(node.Format, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitIsPatternExpression(BoundIsPatternExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            Visit(node.Pattern, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitConstantPattern(BoundConstantPattern node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDiscardPattern(BoundDiscardPattern node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitDeclarationPattern(BoundDeclarationPattern node, Arg arg)
        {
            Visit(node.DeclaredType, arg);
            Visit(node.VariableAccess, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitRecursivePattern(BoundRecursivePattern node, Arg arg)
        {
            Visit(node.DeclaredType, arg);
            VisitList(node.Deconstruction, arg);
            VisitList(node.Properties, arg);
            Visit(node.VariableAccess, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitListPattern(BoundListPattern node, Arg arg)
        {
            VisitList(node.Subpatterns, arg);
            Visit(node.VariableAccess, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitSlicePattern(BoundSlicePattern node, Arg arg)
        {
            Visit(node.Pattern, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitITuplePattern(BoundITuplePattern node, Arg arg)
        {
            VisitList(node.Subpatterns, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitPositionalSubpattern(BoundPositionalSubpattern node, Arg arg)
        {
            Visit(node.Pattern, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitPropertySubpattern(BoundPropertySubpattern node, Arg arg)
        {
            Visit(node.Member, arg);
            Visit(node.Pattern, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitPropertySubpatternMember(BoundPropertySubpatternMember node, Arg arg)
        {
            Visit(node.Receiver, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitTypePattern(BoundTypePattern node, Arg arg)
        {
            Visit(node.DeclaredType, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitBinaryPattern(BoundBinaryPattern node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitNegatedPattern(BoundNegatedPattern node, Arg arg)
        {
            Visit(node.Negated, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitRelationalPattern(BoundRelationalPattern node, Arg arg)
        {
            Visit(node.Value, arg);
            throw ExceptionUtilities.Unreachable();
        }

        public override uint VisitDiscardExpression(BoundDiscardExpression node, Arg arg)
        {
            if (arg.IsRefEscape)
            {
                // PROTOTYPE:
                throw ExceptionUtilities.Unreachable();
            }
            else
            {
                // PROTOTYPE: Why do we need BoundDiscardExpression.ValEscape?
                // Isn't that necessarily LocalScopeDepth? And if it's not, why
                // wasn't CheckValEscape() checking this value?
                return node.ValEscape;
            }
        }

        public override uint VisitThrowExpression(BoundThrowExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            return Binder.CallingMethodScope;
        }

        public override uint VisitOutVariablePendingInference(OutVariablePendingInference node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitDeconstructionVariablePendingInference(DeconstructionVariablePendingInference node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitOutDeconstructVarPendingInference(OutDeconstructVarPendingInference node, Arg arg) => throw ExceptionUtilities.Unreachable();
        public override uint VisitNonConstructorMethodBody(BoundNonConstructorMethodBody node, Arg arg)
        {
            Visit(node.BlockBody, arg);
            Visit(node.ExpressionBody, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitConstructorMethodBody(BoundConstructorMethodBody node, Arg arg)
        {
            Visit(node.Initializer, arg);
            Visit(node.BlockBody, arg);
            Visit(node.ExpressionBody, arg);
            return UnusedEscapeScope;
        }
        public override uint VisitExpressionWithNullability(BoundExpressionWithNullability node, Arg arg)
        {
            Visit(node.Expression, arg);
            throw ExceptionUtilities.Unreachable();
        }
        public override uint VisitWithExpression(BoundWithExpression node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.InitializerExpression, arg);
            throw ExceptionUtilities.Unreachable();
        }

        private uint VisitCall(
            SyntaxNode syntax,
            Symbol symbol,
            BoundExpression? receiver,
            ImmutableArray<ParameterSymbol> parameters,
            ImmutableArray<BoundExpression> argsOpt,
            ImmutableArray<RefKind> argRefKindsOpt,
            ImmutableArray<int> argsToParamsOpt,
            Arg arg)
        {
            // PROTOTYPE: CheckInvocationArgMixingWithUpdatedRules() is not needed
            // if we're just computing the escape scope.
            if (!CheckInvocationArgMixingWithUpdatedRules(
                syntax,
                symbol,
                receiver,
                parameters,
                argsOpt,
                argRefKindsOpt,
                argsToParamsOpt,
                arg.LocalScopeDepth))
            {
                // Avoid duplicate errors from GetInvocationEscapeWithUpdatedRules().
                arg = arg.WithEscapeTo(arg.IsRefEscape, arg.LocalScopeDepth);
            }

            return CheckInvocationEscapeWithUpdatedRules(
                syntax,
                symbol,
                receiver,
                parameters,
                argsOpt,
                argRefKindsOpt,
                argsToParamsOpt,
                checkingReceiver: false /*PROTOTYPE:*/,
                arg);
        }

        private uint CheckInvocationEscapeWithUpdatedRules(
            SyntaxNode syntax,
            Symbol symbol,
            BoundExpression? receiver,
            ImmutableArray<ParameterSymbol> parameters,
            ImmutableArray<BoundExpression> argsOpt,
            ImmutableArray<RefKind> argRefKindsOpt,
            ImmutableArray<int> argsToParamsOpt,
            bool checkingReceiver,
            Arg arg)
        {
            // https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/low-level-struct-improvements.md#rules-method-invocation
            //
            // A value resulting from a method invocation `e1.M(e2, ...)` is *safe-to-escape* from the narrowest of the following scopes:
            // 1. The *calling method*
            // 2. When the return is a `ref struct` the *safe-to-escape* contributed by all argument expressions
            // 3. When the return is a `ref struct` the *ref-safe-to-escape* contributed by all `ref` arguments
            // 
            // A value resulting from a method invocation `ref e1.M(e2, ...)` is *ref-safe-to-escape* the narrowest of the following scopes:
            // 1. The *calling method*
            // 2. The *safe-to-escape* contributed by all argument expressions
            // 3. The *ref-safe-to-escape* contributed by all `ref` arguments

            var argsAndParamsAll = ArrayBuilder<Binder.EscapeValue>.GetInstance();
            Binder.GetFilteredInvocationArgumentsForEscapeWithUpdatedRules(
                symbol,
                receiver,
                parameters,
                argsOpt,
                argRefKindsOpt,
                argsToParamsOpt,
                isInvokedWithRef: true, // to get all arguments
                ignoreArglistRefKinds: true, // https://github.com/dotnet/roslyn/issues/63325: for compatibility with C#10 implementation.
                argsAndParamsAll);

            //by default it is safe to escape
            uint escapeScope = Binder.CallingMethodScope;

            foreach (var argAndParam in argsAndParamsAll)
            {
                var argument = argAndParam.Argument;
                uint argEscape = Visit(argument, arg.WithEscapeTo(isRefEscape: argAndParam.IsRefEscape, arg.EscapeTo));
                if (argEscape > arg.EscapeTo)
                {
                    // For consistency with C#10 implementation, we don't report an additional error
                    // for the receiver. (In both implementations, the call to Check*Escape() above
                    // will have reported a specific escape error for the receiver though.)
                    if ((object)argument != receiver)
                    {
                        Binder.ReportInvocationEscapeError(syntax, symbol, argAndParam.Parameter, checkingReceiver, _diagnostics);
                    }
                }
                escapeScope = Math.Max(escapeScope, argEscape);
                if (escapeScope >= arg.LocalScopeDepth)
                {
                    // can't get any worse
                    break;
                }
            }
            argsAndParamsAll.Free();

            return escapeScope;
        }

        private bool CheckInvocationArgMixingWithUpdatedRules(
            SyntaxNode syntax,
            Symbol symbol,
            BoundExpression? receiverOpt,
            ImmutableArray<ParameterSymbol> parameters,
            ImmutableArray<BoundExpression> argsOpt,
            ImmutableArray<RefKind> argRefKindsOpt,
            ImmutableArray<int> argsToParamsOpt,
            uint scopeOfTheContainingExpression)
        {
            var mixableArguments = ArrayBuilder<Binder.MixableDestination>.GetInstance();
            var escapeValues = ArrayBuilder<Binder.EscapeValue>.GetInstance();
            Binder.GetEscapeValuesForUpdatedRules(
                symbol,
                receiverOpt,
                parameters,
                argsOpt,
                argRefKindsOpt,
                argsToParamsOpt,
                ignoreArglistRefKinds: false,
                mixableArguments,
                escapeValues);

            var valid = true;
            foreach (var mixableArg in mixableArguments)
            {
                uint toArgEscape = Visit(mixableArg.Argument, new Arg(scopeOfTheContainingExpression, isRefEscape: false, escapeTo: scopeOfTheContainingExpression));
                foreach (var (fromParameter, fromArg, escapeKind, isRefEscape) in escapeValues)
                {
                    if (mixableArg.Parameter is not null && object.ReferenceEquals(mixableArg.Parameter, fromParameter))
                    {
                        continue;
                    }

                    // This checks to see if the EscapeValue could ever be assigned to this argument based 
                    // on comparing the EscapeLevel of both. If this could never be assigned due to 
                    // this then we don't need to consider it for MAMM analysis.
                    if (!mixableArg.IsAssignableFrom(escapeKind))
                    {
                        continue;
                    }

                    uint fromArgEscape = Visit(fromArg, new Arg(scopeOfTheContainingExpression, isRefEscape: isRefEscape, escapeTo: toArgEscape));
                    valid = fromArgEscape <= toArgEscape;
                    if (!valid)
                    {
                        string parameterName = Binder.GetInvocationParameterName(fromParameter);
                        Error(_diagnostics, ErrorCode.ERR_CallArgMixing, syntax, symbol, parameterName);
                        break;
                    }
                }

                if (!valid)
                {
                    break;
                }
            }

            inferDeclarationExpressionValEscape();

            mixableArguments.Free();
            escapeValues.Free();
            return valid;

            void inferDeclarationExpressionValEscape()
            {
                // find the widest scope that arguments could safely escape to.
                // use this scope as the inferred STE of declaration expressions.
                var inferredDestinationValEscape = Binder.CallingMethodScope;
                foreach (var (_, fromArg, _, isRefEscape) in escapeValues)
                {
                    uint fromArgEscape = Visit(fromArg, new Arg(scopeOfTheContainingExpression, isRefEscape: isRefEscape, escapeTo: scopeOfTheContainingExpression));
                    inferredDestinationValEscape = Math.Max(inferredDestinationValEscape, fromArgEscape);
                }

                foreach (var (_, fromArg, _, _) in escapeValues)
                {
                    if (Binder.ShouldInferDeclarationExpressionValEscape(fromArg, out var localSymbol))
                    {
                        localSymbol.SetValEscape(inferredDestinationValEscape);
                    }
                }
            }
        }

        private static void Error(BindingDiagnosticBag diagnostics, ErrorCode code, SyntaxNodeOrToken syntax, params object[] args)
        {
            var location = syntax.GetLocation();
            RoslynDebug.Assert(location is object);
            Error(diagnostics, code, location, args);
        }

        private static void Error(BindingDiagnosticBag diagnostics, ErrorCode code, Location location, params object[] args)
        {
            diagnostics.Add(new CSDiagnostic(new CSDiagnosticInfo(code, args), location));
        }
    }
}
