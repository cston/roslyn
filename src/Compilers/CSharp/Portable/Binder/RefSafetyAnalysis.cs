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
    internal sealed class RefSafetyAnalysis : BoundTreeVisitor<RefSafetyAnalysis.Arg, RefSafetyAnalysis.Result>
    {
        internal readonly struct Result
        {
            public readonly uint RefScope;
            public readonly uint ValScope;

            public Result(uint refScope, uint valScope)
            {
                RefScope = refScope;
                ValScope = valScope;
            }

            public static Result Narrower(Result a, Result b)
            {
                return new Result(
                    Math.Max(a.RefScope, b.RefScope),
                    Math.Max(a.ValScope, b.ValScope));
            }
        }

        internal readonly struct Arg
        {
            public readonly uint LocalScopeDepth;
            public readonly bool InUnsafeRegion;
            public readonly uint RefEscapeTo;
            public readonly uint ValEscapeTo;

            public Arg(uint localScopeDepth, bool inUnsafeRegion, uint refEscapeTo, uint valEscapeTo)
            {
                LocalScopeDepth = localScopeDepth;
                InUnsafeRegion = inUnsafeRegion;
                RefEscapeTo = refEscapeTo;
                ValEscapeTo = valEscapeTo;
            }

            public Arg WithScope(Result result) => new Arg(LocalScopeDepth, InUnsafeRegion, refEscapeTo: result.RefScope, valEscapeTo: result.ValScope);

            public Arg WithLocalScope() => new Arg(LocalScopeDepth, InUnsafeRegion, refEscapeTo: LocalScopeDepth, valEscapeTo: LocalScopeDepth);
        }

        private readonly CSharpCompilation _compilation;
        private readonly MethodSymbol _method;
        private readonly bool _useUpdatedEscapeRules;
        private readonly BindingDiagnosticBag _diagnostics;
        private readonly Dictionary<LocalSymbol, Result> _localEscapeScopes;

        public static void Analyze(CSharpCompilation compilation, MethodSymbol method, BoundNode node, BindingDiagnosticBag diagnostics)
        {
            var visitor = new RefSafetyAnalysis(compilation, method, diagnostics);
            visitor.Visit(node, new Arg(localScopeDepth: 2, inUnsafeRegion: false, refEscapeTo: 0, valEscapeTo: 0));
        }

        private RefSafetyAnalysis(CSharpCompilation compilation, MethodSymbol method, BindingDiagnosticBag diagnostics)
        {
            _compilation = compilation;
            _method = method;
            _useUpdatedEscapeRules = _method.ContainingModule.UseUpdatedEscapeRules;
            _diagnostics = diagnostics;
            _localEscapeScopes = new Dictionary<LocalSymbol, Result>();
        }

        public override Result DefaultVisit(BoundNode node, Arg arg)
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

        public override Result VisitFieldEqualsValue(BoundFieldEqualsValue node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitPropertyEqualsValue(BoundPropertyEqualsValue node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitParameterEqualsValue(BoundParameterEqualsValue node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitGlobalStatementInitializer(BoundGlobalStatementInitializer node, Arg arg)
        {
            Visit(node.Statement, arg);
            return default;
        }
        public override Result VisitValuePlaceholder(BoundValuePlaceholder node, Arg arg) => default;
        public override Result VisitDeconstructValuePlaceholder(BoundDeconstructValuePlaceholder node, Arg arg) => default;
        public override Result VisitTupleOperandPlaceholder(BoundTupleOperandPlaceholder node, Arg arg) => default;
        public override Result VisitAwaitableValuePlaceholder(BoundAwaitableValuePlaceholder node, Arg arg) => default;
        public override Result VisitDisposableValuePlaceholder(BoundDisposableValuePlaceholder node, Arg arg) => default;
        public override Result VisitObjectOrCollectionValuePlaceholder(BoundObjectOrCollectionValuePlaceholder node, Arg arg) => default;
        public override Result VisitImplicitIndexerValuePlaceholder(BoundImplicitIndexerValuePlaceholder node, Arg arg) => default;
        public override Result VisitImplicitIndexerReceiverPlaceholder(BoundImplicitIndexerReceiverPlaceholder node, Arg arg) => default;
        public override Result VisitListPatternReceiverPlaceholder(BoundListPatternReceiverPlaceholder node, Arg arg) => default;
        public override Result VisitListPatternIndexPlaceholder(BoundListPatternIndexPlaceholder node, Arg arg) => default;
        public override Result VisitSlicePatternReceiverPlaceholder(BoundSlicePatternReceiverPlaceholder node, Arg arg) => default;
        public override Result VisitSlicePatternRangePlaceholder(BoundSlicePatternRangePlaceholder node, Arg arg) => default;
        public override Result VisitDup(BoundDup node, Arg arg) => default;
        public override Result VisitPassByCopy(BoundPassByCopy node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitBadExpression(BoundBadExpression node, Arg arg)
        {
            VisitList(node.ChildBoundNodes, arg);
            return default;
        }
        public override Result VisitBadStatement(BoundBadStatement node, Arg arg)
        {
            VisitList(node.ChildBoundNodes, arg);
            return default;
        }
        public override Result VisitExtractedFinallyBlock(BoundExtractedFinallyBlock node, Arg arg)
        {
            Visit(node.FinallyBlock, arg);
            return default;
        }
        public override Result VisitTypeExpression(BoundTypeExpression node, Arg arg)
        {
            Visit(node.BoundContainingTypeOpt, arg);
            VisitList(node.BoundDimensionsOpt, arg);
            return default;
        }
        public override Result VisitTypeOrValueExpression(BoundTypeOrValueExpression node, Arg arg) => default;
        public override Result VisitNamespaceExpression(BoundNamespaceExpression node, Arg arg) => default;
        public override Result VisitUnaryOperator(BoundUnaryOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitIncrementOperator(BoundIncrementOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitAddressOfOperator(BoundAddressOfOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitUnconvertedAddressOfOperator(BoundUnconvertedAddressOfOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitFunctionPointerLoad(BoundFunctionPointerLoad node, Arg arg) => default;
        public override Result VisitPointerIndirectionOperator(BoundPointerIndirectionOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitPointerElementAccess(BoundPointerElementAccess node, Arg arg)
        {
            Visit(node.Expression, arg);
            Visit(node.Index, arg);
            return default;
        }
        public override Result VisitFunctionPointerInvocation(BoundFunctionPointerInvocation node, Arg arg)
        {
            Visit(node.InvokedExpression, arg);
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitRefTypeOperator(BoundRefTypeOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitMakeRefOperator(BoundMakeRefOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitRefValueOperator(BoundRefValueOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitFromEndIndexExpression(BoundFromEndIndexExpression node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitRangeExpression(BoundRangeExpression node, Arg arg)
        {
            Visit(node.LeftOperandOpt, arg);
            Visit(node.RightOperandOpt, arg);
            return default;
        }
        public override Result VisitBinaryOperator(BoundBinaryOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            return default;
        }
        public override Result VisitTupleBinaryOperator(BoundTupleBinaryOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            return default;
        }
        public override Result VisitUserDefinedConditionalLogicalOperator(BoundUserDefinedConditionalLogicalOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            return default;
        }
        public override Result VisitCompoundAssignmentOperator(BoundCompoundAssignmentOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            return default;
        }

        public override Result VisitAssignmentOperator(BoundAssignmentOperator node, Arg arg)
        {
            var op1 = node.Left;
            var op2 = node.Right;
            var leftResult = Visit(op1, arg);

            if (node.IsRef)
            {
                var rightResult = VisitExpression(op2, isRef: true, arg.WithLocalScope());
                var leftEscape = leftResult.RefScope;
                var rightEscape = rightResult.RefScope;
                if (leftEscape < rightEscape)
                {
                    var errorCode = (rightEscape, arg.InUnsafeRegion) switch
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
            }
            else
            {
                _ = VisitExpression(op2, isRef: node.IsRef, arg.WithScope(leftResult));
            }

            return leftResult;

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
        public override Result VisitDeconstructionAssignmentOperator(BoundDeconstructionAssignmentOperator node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            return default;
        }
        public override Result VisitNullCoalescingOperator(BoundNullCoalescingOperator node, Arg arg)
        {
            Visit(node.LeftOperand, arg);
            Visit(node.RightOperand, arg);
            return default;
        }
        public override Result VisitNullCoalescingAssignmentOperator(BoundNullCoalescingAssignmentOperator node, Arg arg)
        {
            Visit(node.LeftOperand, arg);
            Visit(node.RightOperand, arg);
            return default;
        }
        public override Result VisitUnconvertedConditionalOperator(BoundUnconvertedConditionalOperator node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Consequence, arg);
            Visit(node.Alternative, arg);
            return default;
        }
        public override Result VisitConditionalOperator(BoundConditionalOperator node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Consequence, arg);
            Visit(node.Alternative, arg);
            return default;
        }
        public override Result VisitArrayAccess(BoundArrayAccess node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.Indices, arg);
            return default;
        }
        public override Result VisitArrayLength(BoundArrayLength node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitAwaitableInfo(BoundAwaitableInfo node, Arg arg)
        {
            Visit(node.AwaitableInstancePlaceholder, arg);
            Visit(node.GetAwaiter, arg);
            return default;
        }
        public override Result VisitAwaitExpression(BoundAwaitExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            Visit(node.AwaitableInfo, arg);
            return default;
        }
        public override Result VisitTypeOfOperator(BoundTypeOfOperator node, Arg arg)
        {
            Visit(node.SourceType, arg);
            return default;
        }
        public override Result VisitMethodDefIndex(BoundMethodDefIndex node, Arg arg) => default;
        public override Result VisitMaximumMethodDefIndex(BoundMaximumMethodDefIndex node, Arg arg) => default;
        public override Result VisitInstrumentationPayloadRoot(BoundInstrumentationPayloadRoot node, Arg arg) => default;
        public override Result VisitModuleVersionId(BoundModuleVersionId node, Arg arg) => default;
        public override Result VisitModuleVersionIdString(BoundModuleVersionIdString node, Arg arg) => default;
        public override Result VisitSourceDocumentIndex(BoundSourceDocumentIndex node, Arg arg) => default;
        public override Result VisitMethodInfo(BoundMethodInfo node, Arg arg) => default;
        public override Result VisitFieldInfo(BoundFieldInfo node, Arg arg) => default;
        public override Result VisitDefaultLiteral(BoundDefaultLiteral node, Arg arg) => default;
        public override Result VisitDefaultExpression(BoundDefaultExpression node, Arg arg) => default;
        public override Result VisitIsOperator(BoundIsOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            Visit(node.TargetType, arg);
            return default;
        }
        public override Result VisitAsOperator(BoundAsOperator node, Arg arg)
        {
            Visit(node.Operand, arg);
            Visit(node.TargetType, arg);
            return default;
        }
        public override Result VisitSizeOfOperator(BoundSizeOfOperator node, Arg arg)
        {
            Visit(node.SourceType, arg);
            return default;
        }
        public override Result VisitConversion(BoundConversion node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitReadOnlySpanFromArray(BoundReadOnlySpanFromArray node, Arg arg)
        {
            Visit(node.Operand, arg);
            return default;
        }
        public override Result VisitArgList(BoundArgList node, Arg arg) => default;
        public override Result VisitArgListOperator(BoundArgListOperator node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitFixedLocalCollectionInitializer(BoundFixedLocalCollectionInitializer node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitSequencePoint(BoundSequencePoint node, Arg arg)
        {
            Visit(node.StatementOpt, arg);
            return default;
        }
        public override Result VisitSequencePointWithSpan(BoundSequencePointWithSpan node, Arg arg)
        {
            Visit(node.StatementOpt, arg);
            return default;
        }
        public override Result VisitSavePreviousSequencePoint(BoundSavePreviousSequencePoint node, Arg arg) => default;
        public override Result VisitRestorePreviousSequencePoint(BoundRestorePreviousSequencePoint node, Arg arg) => default;
        public override Result VisitStepThroughSequencePoint(BoundStepThroughSequencePoint node, Arg arg) => default;

        public override Result VisitBlock(BoundBlock node, Arg arg)
        {
            VisitList(node.Statements, new Arg(arg.LocalScopeDepth + 1, arg.InUnsafeRegion, arg.RefEscapeTo, arg.ValEscapeTo));
            return default;
        }

        public override Result VisitScope(BoundScope node, Arg arg)
        {
            VisitList(node.Statements, arg);
            return default;
        }
        public override Result VisitStateMachineScope(BoundStateMachineScope node, Arg arg)
        {
            Visit(node.Statement, arg);
            return default;
        }

        public override Result VisitLocalDeclaration(BoundLocalDeclaration node, Arg arg)
        {
            Visit(node.DeclaredTypeOpt, arg);
            var result = Visit(node.InitializerOpt, arg);
            result = new Result(arg.LocalScopeDepth, result.ValScope);
            _localEscapeScopes.Add(node.LocalSymbol, result);
            VisitList(node.ArgumentsOpt, arg);
            return default;
        }

        public override Result VisitMultipleLocalDeclarations(BoundMultipleLocalDeclarations node, Arg arg)
        {
            VisitList(node.LocalDeclarations, arg);
            return default;
        }
        public override Result VisitUsingLocalDeclarations(BoundUsingLocalDeclarations node, Arg arg)
        {
            Visit(node.AwaitOpt, arg);
            VisitList(node.LocalDeclarations, arg);
            return default;
        }
        public override Result VisitLocalFunctionStatement(BoundLocalFunctionStatement node, Arg arg)
        {
            Visit(node.BlockBody, arg);
            Visit(node.ExpressionBody, arg);
            return default;
        }
        public override Result VisitNoOpStatement(BoundNoOpStatement node, Arg arg) => default;
        public override Result VisitReturnStatement(BoundReturnStatement node, Arg arg)
        {
            Visit(node.ExpressionOpt, arg);
            return default;
        }
        public override Result VisitYieldReturnStatement(BoundYieldReturnStatement node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitYieldBreakStatement(BoundYieldBreakStatement node, Arg arg) => default;
        public override Result VisitThrowStatement(BoundThrowStatement node, Arg arg)
        {
            Visit(node.ExpressionOpt, arg);
            return default;
        }
        public override Result VisitExpressionStatement(BoundExpressionStatement node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitBreakStatement(BoundBreakStatement node, Arg arg) => default;
        public override Result VisitContinueStatement(BoundContinueStatement node, Arg arg) => default;
        public override Result VisitSwitchStatement(BoundSwitchStatement node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.SwitchSections, arg);
            Visit(node.DefaultLabel, arg);
            return default;
        }
        public override Result VisitSwitchDispatch(BoundSwitchDispatch node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitIfStatement(BoundIfStatement node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Consequence, arg);
            Visit(node.AlternativeOpt, arg);
            return default;
        }
        public override Result VisitDoStatement(BoundDoStatement node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitWhileStatement(BoundWhileStatement node, Arg arg)
        {
            Visit(node.Condition, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitForStatement(BoundForStatement node, Arg arg)
        {
            Visit(node.Initializer, arg);
            Visit(node.Condition, arg);
            Visit(node.Increment, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitForEachStatement(BoundForEachStatement node, Arg arg)
        {
            Visit(node.IterationVariableType, arg);
            Visit(node.IterationErrorExpressionOpt, arg);
            Visit(node.Expression, arg);
            Visit(node.DeconstructionOpt, arg);
            Visit(node.AwaitOpt, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitForEachDeconstructStep(BoundForEachDeconstructStep node, Arg arg)
        {
            Visit(node.DeconstructionAssignment, arg);
            Visit(node.TargetPlaceholder, arg);
            return default;
        }
        public override Result VisitUsingStatement(BoundUsingStatement node, Arg arg)
        {
            Visit(node.DeclarationsOpt, arg);
            Visit(node.ExpressionOpt, arg);
            Visit(node.Body, arg);
            Visit(node.AwaitOpt, arg);
            return default;
        }
        public override Result VisitFixedStatement(BoundFixedStatement node, Arg arg)
        {
            Visit(node.Declarations, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitLockStatement(BoundLockStatement node, Arg arg)
        {
            Visit(node.Argument, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitTryStatement(BoundTryStatement node, Arg arg)
        {
            Visit(node.TryBlock, arg);
            VisitList(node.CatchBlocks, arg);
            Visit(node.FinallyBlockOpt, arg);
            return default;
        }
        public override Result VisitCatchBlock(BoundCatchBlock node, Arg arg)
        {
            Visit(node.ExceptionSourceOpt, arg);
            Visit(node.ExceptionFilterPrologueOpt, arg);
            Visit(node.ExceptionFilterOpt, arg);
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitLiteral(BoundLiteral node, Arg arg) => default;
        public override Result VisitUtf8String(BoundUtf8String node, Arg arg) => default;
        public override Result VisitThisReference(BoundThisReference node, Arg arg) => default;
        public override Result VisitPreviousSubmissionReference(BoundPreviousSubmissionReference node, Arg arg) => default;
        public override Result VisitHostObjectMemberReference(BoundHostObjectMemberReference node, Arg arg) => default;
        public override Result VisitBaseReference(BoundBaseReference node, Arg arg) => default;

        public override Result VisitLocal(BoundLocal node, Arg arg)
        {
            var localSymbol = node.LocalSymbol;
            var result =_localEscapeScopes[localSymbol];
            if (result.ValScope > arg.ValEscapeTo)
            {
                Error(_diagnostics, arg.InUnsafeRegion ? ErrorCode.WRN_EscapeVariable : ErrorCode.ERR_EscapeVariable, node.Syntax, localSymbol);
            }
            return result;
        }

        public override Result VisitPseudoVariable(BoundPseudoVariable node, Arg arg) => default;
        public override Result VisitRangeVariable(BoundRangeVariable node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }

        public override Result VisitParameter(BoundParameter node, Arg arg)
        {
            return default;
        }

        public override Result VisitLabelStatement(BoundLabelStatement node, Arg arg) => default;
        public override Result VisitGotoStatement(BoundGotoStatement node, Arg arg)
        {
            Visit(node.CaseExpressionOpt, arg);
            Visit(node.LabelExpressionOpt, arg);
            return default;
        }
        public override Result VisitLabeledStatement(BoundLabeledStatement node, Arg arg)
        {
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitLabel(BoundLabel node, Arg arg) => default;
        public override Result VisitStatementList(BoundStatementList node, Arg arg)
        {
            VisitList(node.Statements, arg);
            return default;
        }
        public override Result VisitConditionalGoto(BoundConditionalGoto node, Arg arg)
        {
            Visit(node.Condition, arg);
            return default;
        }
        public override Result VisitSwitchExpressionArm(BoundSwitchExpressionArm node, Arg arg)
        {
            Visit(node.Pattern, arg);
            Visit(node.WhenClause, arg);
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitUnconvertedSwitchExpression(BoundUnconvertedSwitchExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.SwitchArms, arg);
            return default;
        }
        public override Result VisitConvertedSwitchExpression(BoundConvertedSwitchExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.SwitchArms, arg);
            return default;
        }
        public override Result VisitDecisionDag(BoundDecisionDag node, Arg arg)
        {
            Visit(node.RootNode, arg);
            return default;
        }
        public override Result VisitEvaluationDecisionDagNode(BoundEvaluationDecisionDagNode node, Arg arg)
        {
            Visit(node.Evaluation, arg);
            Visit(node.Next, arg);
            return default;
        }
        public override Result VisitTestDecisionDagNode(BoundTestDecisionDagNode node, Arg arg)
        {
            Visit(node.Test, arg);
            Visit(node.WhenTrue, arg);
            Visit(node.WhenFalse, arg);
            return default;
        }
        public override Result VisitWhenDecisionDagNode(BoundWhenDecisionDagNode node, Arg arg)
        {
            Visit(node.WhenExpression, arg);
            Visit(node.WhenTrue, arg);
            Visit(node.WhenFalse, arg);
            return default;
        }
        public override Result VisitLeafDecisionDagNode(BoundLeafDecisionDagNode node, Arg arg) => default;
        public override Result VisitDagTemp(BoundDagTemp node, Arg arg)
        {
            Visit(node.Source, arg);
            return default;
        }
        public override Result VisitDagTypeTest(BoundDagTypeTest node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagNonNullTest(BoundDagNonNullTest node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagExplicitNullTest(BoundDagExplicitNullTest node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagValueTest(BoundDagValueTest node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagRelationalTest(BoundDagRelationalTest node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagDeconstructEvaluation(BoundDagDeconstructEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagTypeEvaluation(BoundDagTypeEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagFieldEvaluation(BoundDagFieldEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagPropertyEvaluation(BoundDagPropertyEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagIndexEvaluation(BoundDagIndexEvaluation node, Arg arg)
        {
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagIndexerEvaluation(BoundDagIndexerEvaluation node, Arg arg)
        {
            Visit(node.LengthTemp, arg);
            Visit(node.IndexerAccess, arg);
            Visit(node.ReceiverPlaceholder, arg);
            Visit(node.ArgumentPlaceholder, arg);
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagSliceEvaluation(BoundDagSliceEvaluation node, Arg arg)
        {
            Visit(node.LengthTemp, arg);
            Visit(node.IndexerAccess, arg);
            Visit(node.ReceiverPlaceholder, arg);
            Visit(node.ArgumentPlaceholder, arg);
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitDagAssignmentEvaluation(BoundDagAssignmentEvaluation node, Arg arg)
        {
            Visit(node.Target, arg);
            Visit(node.Input, arg);
            return default;
        }
        public override Result VisitSwitchSection(BoundSwitchSection node, Arg arg)
        {
            VisitList(node.SwitchLabels, arg);
            VisitList(node.Statements, arg);
            return default;
        }
        public override Result VisitSwitchLabel(BoundSwitchLabel node, Arg arg)
        {
            Visit(node.Pattern, arg);
            Visit(node.WhenClause, arg);
            return default;
        }
        public override Result VisitSequencePointExpression(BoundSequencePointExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitSequence(BoundSequence node, Arg arg)
        {
            VisitList(node.SideEffects, arg);
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitSpillSequence(BoundSpillSequence node, Arg arg)
        {
            VisitList(node.SideEffects, arg);
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitDynamicMemberAccess(BoundDynamicMemberAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            return default;
        }
        public override Result VisitDynamicInvocation(BoundDynamicInvocation node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitConditionalAccess(BoundConditionalAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.AccessExpression, arg);
            return default;
        }
        public override Result VisitLoweredConditionalAccess(BoundLoweredConditionalAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.WhenNotNull, arg);
            Visit(node.WhenNullOpt, arg);
            return default;
        }
        public override Result VisitConditionalReceiver(BoundConditionalReceiver node, Arg arg) => default;
        public override Result VisitComplexConditionalReceiver(BoundComplexConditionalReceiver node, Arg arg)
        {
            Visit(node.ValueTypeReceiver, arg);
            Visit(node.ReferenceTypeReceiver, arg);
            return default;
        }
        public override Result VisitMethodGroup(BoundMethodGroup node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitPropertyGroup(BoundPropertyGroup node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitCall(BoundCall node, Arg arg)
        {
            return VisitCall(node, isRef: false, arg);
        }

        private Result VisitCall(BoundCall node, bool isRef, Arg arg)
        {
            var method = node.Method;
            return GetInvocationEscapeWithUpdatedRules(
                method,
                node.ReceiverOpt,
                method.Parameters,
                node.Arguments,
                node.ArgumentRefKindsOpt,
                node.ArgsToParamsOpt,
                arg);
        }

        private Result VisitExpression(BoundExpression expr, bool isRef, Arg arg)
        {
            return expr switch
            {
                BoundCall call => VisitCall(call, isRef: isRef, arg),
                _ => Visit(expr, arg),
            };
        }

        public override Result VisitEventAssignmentOperator(BoundEventAssignmentOperator node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            Visit(node.Argument, arg);
            return default;
        }
        public override Result VisitAttribute(BoundAttribute node, Arg arg)
        {
            VisitList(node.ConstructorArguments, arg);
            VisitList(node.NamedArguments, arg);
            return default;
        }
        public override Result VisitUnconvertedObjectCreationExpression(BoundUnconvertedObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            return default;
        }

        public override Result VisitObjectCreationExpression(BoundObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            var initializerResult = Visit(node.InitializerExpressionOpt, arg);

            var constructorSymbol = node.Constructor;
            var result = GetInvocationEscapeWithUpdatedRules(
                constructorSymbol,
                null,
                constructorSymbol.Parameters,
                node.Arguments,
                node.ArgumentRefKindsOpt,
                node.ArgsToParamsOpt,
                arg);

            return Result.Narrower(result, initializerResult);
        }

        public override Result VisitTupleLiteral(BoundTupleLiteral node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitConvertedTupleLiteral(BoundConvertedTupleLiteral node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitDynamicObjectCreationExpression(BoundDynamicObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            Visit(node.InitializerExpressionOpt, arg);
            return default;
        }
        public override Result VisitNoPiaObjectCreationExpression(BoundNoPiaObjectCreationExpression node, Arg arg)
        {
            Visit(node.InitializerExpressionOpt, arg);
            return default;
        }
        public override Result VisitObjectInitializerExpression(BoundObjectInitializerExpression node, Arg arg)
        {
            Visit(node.Placeholder, arg);
            VisitList(node.Initializers, arg);
            return default;
        }
        public override Result VisitObjectInitializerMember(BoundObjectInitializerMember node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitDynamicObjectInitializerMember(BoundDynamicObjectInitializerMember node, Arg arg) => default;
        public override Result VisitCollectionInitializerExpression(BoundCollectionInitializerExpression node, Arg arg)
        {
            Visit(node.Placeholder, arg);
            VisitList(node.Initializers, arg);
            return default;
        }
        public override Result VisitCollectionElementInitializer(BoundCollectionElementInitializer node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            Visit(node.ImplicitReceiverOpt, arg);
            return default;
        }
        public override Result VisitDynamicCollectionElementInitializer(BoundDynamicCollectionElementInitializer node, Arg arg)
        {
            Visit(node.Expression, arg);
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitImplicitReceiver(BoundImplicitReceiver node, Arg arg) => default;
        public override Result VisitAnonymousObjectCreationExpression(BoundAnonymousObjectCreationExpression node, Arg arg)
        {
            VisitList(node.Arguments, arg);
            VisitList(node.Declarations, arg);
            return default;
        }
        public override Result VisitAnonymousPropertyDeclaration(BoundAnonymousPropertyDeclaration node, Arg arg) => default;
        public override Result VisitNewT(BoundNewT node, Arg arg)
        {
            Visit(node.InitializerExpressionOpt, arg);
            return default;
        }
        public override Result VisitDelegateCreationExpression(BoundDelegateCreationExpression node, Arg arg)
        {
            Visit(node.Argument, arg);
            return default;
        }
        public override Result VisitArrayCreation(BoundArrayCreation node, Arg arg)
        {
            VisitList(node.Bounds, arg);
            Visit(node.InitializerOpt, arg);
            return default;
        }
        public override Result VisitArrayInitialization(BoundArrayInitialization node, Arg arg)
        {
            VisitList(node.Initializers, arg);
            return default;
        }
        public override Result VisitStackAllocArrayCreation(BoundStackAllocArrayCreation node, Arg arg)
        {
            Visit(node.Count, arg);
            Visit(node.InitializerOpt, arg);
            return default;
        }
        public override Result VisitConvertedStackAllocExpression(BoundConvertedStackAllocExpression node, Arg arg)
        {
            Visit(node.Count, arg);
            Visit(node.InitializerOpt, arg);
            return default;
        }
        public override Result VisitFieldAccess(BoundFieldAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitHoistedFieldAccess(BoundHoistedFieldAccess node, Arg arg) => default;
        public override Result VisitPropertyAccess(BoundPropertyAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitEventAccess(BoundEventAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitIndexerAccess(BoundIndexerAccess node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitImplicitIndexerAccess(BoundImplicitIndexerAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.Argument, arg);
            return default;
        }
        public override Result VisitDynamicIndexerAccess(BoundDynamicIndexerAccess node, Arg arg)
        {
            Visit(node.Receiver, arg);
            VisitList(node.Arguments, arg);
            return default;
        }
        public override Result VisitLambda(BoundLambda node, Arg arg)
        {
            Visit(node.Body, arg);
            return default;
        }
        public override Result VisitUnboundLambda(UnboundLambda node, Arg arg) => default;
        public override Result VisitQueryClause(BoundQueryClause node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitTypeOrInstanceInitializers(BoundTypeOrInstanceInitializers node, Arg arg)
        {
            VisitList(node.Statements, arg);
            return default;
        }
        public override Result VisitNameOfOperator(BoundNameOfOperator node, Arg arg)
        {
            Visit(node.Argument, arg);
            return default;
        }
        public override Result VisitUnconvertedInterpolatedString(BoundUnconvertedInterpolatedString node, Arg arg)
        {
            VisitList(node.Parts, arg);
            return default;
        }
        public override Result VisitInterpolatedString(BoundInterpolatedString node, Arg arg)
        {
            VisitList(node.Parts, arg);
            return default;
        }
        public override Result VisitInterpolatedStringHandlerPlaceholder(BoundInterpolatedStringHandlerPlaceholder node, Arg arg) => default;
        public override Result VisitInterpolatedStringArgumentPlaceholder(BoundInterpolatedStringArgumentPlaceholder node, Arg arg) => default;
        public override Result VisitStringInsert(BoundStringInsert node, Arg arg)
        {
            Visit(node.Value, arg);
            Visit(node.Alignment, arg);
            Visit(node.Format, arg);
            return default;
        }
        public override Result VisitIsPatternExpression(BoundIsPatternExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            Visit(node.Pattern, arg);
            return default;
        }
        public override Result VisitConstantPattern(BoundConstantPattern node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitDiscardPattern(BoundDiscardPattern node, Arg arg) => default;
        public override Result VisitDeclarationPattern(BoundDeclarationPattern node, Arg arg)
        {
            Visit(node.DeclaredType, arg);
            Visit(node.VariableAccess, arg);
            return default;
        }
        public override Result VisitRecursivePattern(BoundRecursivePattern node, Arg arg)
        {
            Visit(node.DeclaredType, arg);
            VisitList(node.Deconstruction, arg);
            VisitList(node.Properties, arg);
            Visit(node.VariableAccess, arg);
            return default;
        }
        public override Result VisitListPattern(BoundListPattern node, Arg arg)
        {
            VisitList(node.Subpatterns, arg);
            Visit(node.VariableAccess, arg);
            return default;
        }
        public override Result VisitSlicePattern(BoundSlicePattern node, Arg arg)
        {
            Visit(node.Pattern, arg);
            return default;
        }
        public override Result VisitITuplePattern(BoundITuplePattern node, Arg arg)
        {
            VisitList(node.Subpatterns, arg);
            return default;
        }
        public override Result VisitPositionalSubpattern(BoundPositionalSubpattern node, Arg arg)
        {
            Visit(node.Pattern, arg);
            return default;
        }
        public override Result VisitPropertySubpattern(BoundPropertySubpattern node, Arg arg)
        {
            Visit(node.Member, arg);
            Visit(node.Pattern, arg);
            return default;
        }
        public override Result VisitPropertySubpatternMember(BoundPropertySubpatternMember node, Arg arg)
        {
            Visit(node.Receiver, arg);
            return default;
        }
        public override Result VisitTypePattern(BoundTypePattern node, Arg arg)
        {
            Visit(node.DeclaredType, arg);
            return default;
        }
        public override Result VisitBinaryPattern(BoundBinaryPattern node, Arg arg)
        {
            Visit(node.Left, arg);
            Visit(node.Right, arg);
            return default;
        }
        public override Result VisitNegatedPattern(BoundNegatedPattern node, Arg arg)
        {
            Visit(node.Negated, arg);
            return default;
        }
        public override Result VisitRelationalPattern(BoundRelationalPattern node, Arg arg)
        {
            Visit(node.Value, arg);
            return default;
        }
        public override Result VisitDiscardExpression(BoundDiscardExpression node, Arg arg) => default;
        public override Result VisitThrowExpression(BoundThrowExpression node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitOutVariablePendingInference(OutVariablePendingInference node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitDeconstructionVariablePendingInference(DeconstructionVariablePendingInference node, Arg arg)
        {
            Visit(node.ReceiverOpt, arg);
            return default;
        }
        public override Result VisitOutDeconstructVarPendingInference(OutDeconstructVarPendingInference node, Arg arg) => default;
        public override Result VisitNonConstructorMethodBody(BoundNonConstructorMethodBody node, Arg arg)
        {
            Visit(node.BlockBody, arg);
            Visit(node.ExpressionBody, arg);
            return default;
        }
        public override Result VisitConstructorMethodBody(BoundConstructorMethodBody node, Arg arg)
        {
            Visit(node.Initializer, arg);
            Visit(node.BlockBody, arg);
            Visit(node.ExpressionBody, arg);
            return default;
        }
        public override Result VisitExpressionWithNullability(BoundExpressionWithNullability node, Arg arg)
        {
            Visit(node.Expression, arg);
            return default;
        }
        public override Result VisitWithExpression(BoundWithExpression node, Arg arg)
        {
            Visit(node.Receiver, arg);
            Visit(node.InitializerExpression, arg);
            return default;
        }

        private Result GetInvocationEscapeWithUpdatedRules(
            Symbol symbol,
            BoundExpression? receiver,
            ImmutableArray<ParameterSymbol> parameters,
            ImmutableArray<BoundExpression> argsOpt,
            ImmutableArray<RefKind> argRefKindsOpt,
            ImmutableArray<int> argsToParamsOpt,
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

            // In short, request all arguments and calculate the ref-safe-to-escape above.
            // And safe-to-escape is the same value if the return is a ref struct, otherwise calling method.

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

            uint refScope = Binder.CallingMethodScope;
            foreach (var argAndParam in argsAndParamsAll)
            {
                var argument = argAndParam.Argument;
                var argResult = Visit(argument, arg);
                uint argEscape = argAndParam.IsRefEscape ? argResult.RefScope : argResult.ValScope;
                refScope = Math.Max(refScope, argEscape);
                if (refScope >= arg.LocalScopeDepth)
                {
                    // can't get any worse
                    break;
                }
            }
            argsAndParamsAll.Free();

            uint valScope = Binder.HasRefLikeReturn(symbol) ? refScope : Binder.CallingMethodScope;
            return new Result(refScope, valScope);
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
