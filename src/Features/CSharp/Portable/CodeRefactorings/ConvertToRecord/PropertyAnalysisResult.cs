﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.CodeRefactorings.ConvertToRecord
{
    /// <summary>
    /// Represents a property that should be added as a positional parameter
    /// </summary>
    /// <param name="Syntax">Original declaration, if within this class.
    /// Null iff <see cref="IsInherited"/> is true</param>
    /// <param name="Symbol">Symbol of the property</param>
    /// <param name="KeepAsOverride">Whether we should keep the original declaration present</param>
    /// <param name="IsInherited">Whether this property is inherited from another base record</param>
    internal record PropertyAnalysisResult(
        PropertyDeclarationSyntax? Syntax,
        IPropertySymbol Symbol,
        bool KeepAsOverride,
        bool IsInherited)
    {

        public static ImmutableArray<PropertyAnalysisResult> AnalyzeProperties(
            ImmutableArray<PropertyDeclarationSyntax> properties,
            INamedTypeSymbol type,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            // get all declared property symbols, put inherited property symbols first
            var symbols = properties
                .SelectAsArray(p => (IPropertySymbol)semanticModel.GetRequiredDeclaredSymbol(p, cancellationToken));

            // The user may not know about init or be converting code from before init was introduced.
            // In this case we can convert set properties to init ones
            var allowSetToInitConversion = !symbols
                .Any(symbol => symbol.SetMethod is IMethodSymbol { IsInitOnly: true });

            var declaredResults = properties.ZipAsArray(symbols, (syntax, symbol)
                => ShouldConvertProperty(syntax, symbol, type) switch
                {
                    ConvertStatus.DoNotConvert => null,
                    ConvertStatus.Override
                        => new PropertyAnalysisResult(syntax, symbol, KeepAsOverride: true, IsInherited: false),
                    ConvertStatus.OverrideIfConvertingSetToInit
                        => new PropertyAnalysisResult(syntax, symbol, !allowSetToInitConversion, IsInherited: false),
                    ConvertStatus.AlwaysConvert
                        => new PropertyAnalysisResult(syntax, symbol, KeepAsOverride: false, IsInherited: false),
                    _ => throw ExceptionUtilities.Unreachable,
                }).WhereNotNull().AsImmutable();

            // add inherited properties from a potential base record
            var inheritedProperties = GetInheritedPositionalParams(type, cancellationToken);
            return declaredResults.Concat(
                inheritedProperties.SelectAsArray(property
                    => new PropertyAnalysisResult(
                        Syntax: null,
                        property,
                        KeepAsOverride: false,
                        IsInherited: true)));
        }

        private static ImmutableArray<IPropertySymbol> GetInheritedPositionalParams(
            INamedTypeSymbol currentType,
            CancellationToken cancellationToken)
        {
            var baseType = currentType.BaseType;
            if (baseType != null && baseType.TryGetRecordPrimaryConstructor(out var basePrimary))
            {
                return basePrimary.Parameters
                    .SelectAsArray(param => param.GetAssociatedSynthesizedRecordProperty(cancellationToken)!);
            }

            return ImmutableArray<IPropertySymbol>.Empty;
        }

        // for each property, say whether we can convert
        // to primary constructor parameter or not (and whether it would imply changes)
        private enum ConvertStatus
        {
            // no way we can convert this
            DoNotConvert,
            // we can convert this because we feel it would be used in a primary constructor,
            // but some accessibility is non-default and we want to override
            Override,
            // we can convert this if we see that the user only ever uses set (not init)
            // otherwise we should give an override
            OverrideIfConvertingSetToInit,
            // we can convert this without changing the meaning 
            AlwaysConvert
        }

        private static ConvertStatus ShouldConvertProperty(
            PropertyDeclarationSyntax property,
            IPropertySymbol propertySymbol,
            INamedTypeSymbol containingType)
        {
            // properties with identifiers or expression bodies are too complex to move
            // unimplemented or static properties shouldn't be in a constructor
            if (property.Initializer != null ||
                property.ExpressionBody != null ||
                propertySymbol.IsAbstract ||
                propertySymbol.IsStatic)
            {
                return ConvertStatus.DoNotConvert;
            }

            var propAccessibility = propertySymbol.DeclaredAccessibility;
            // more restrictive than internal (protected, private, private protected, or unspecified (private by default)).
            // We allow internal props to be converted to public auto-generated ones
            // because it's still as accessible as a constructor would be from outside the class.
            if (propAccessibility < Accessibility.Internal)
            {
                return ConvertStatus.DoNotConvert;
            }

            // no accessors declared
            if (property.AccessorList == null || property.AccessorList.Accessors.IsEmpty())
            {
                return ConvertStatus.DoNotConvert;
            }

            // When we convert to primary constructor parameters, the auto-generated properties have default behavior
            // Here are the cases where we wouldn't substantially change default behavior
            // - No accessors can have any explicit implementation or modifiers
            //   - This is because it would indicate complex functionality or explicit hiding which is not default
            // - class records and readonly struct records must have:
            //   - public get accessor
            //   - optionally a public init accessor
            //     - note: if this is not provided the user can still initialize the property in the constructor,
            //             so it's like init but without the user ability to initialize outside the constructor
            // - for non-readonly structs, we must have:
            //   - public get accessor
            //   - public set accessor
            // If the user has a private/protected set method, it could still make sense to be a primary constructor
            // but we should provide the override in case the user sets the property from within the class
            var getAccessor = propertySymbol.GetMethod;
            var setAccessor = propertySymbol.SetMethod;
            var accessors = property.AccessorList.Accessors;
            if (accessors.Any(a => a.Body != null || a.ExpressionBody != null) ||
                getAccessor == null ||
                // private get means they probably don't want it seen from the constructor
                getAccessor.DeclaredAccessibility < Accessibility.Internal)
            {
                return ConvertStatus.DoNotConvert;
            }

            // we consider a internal (by default) get on an internal property as public
            // but if the user specifically declares a more restrictive accessibility
            // it would indicate they want to keep it safer than the rest of the property
            // and we should respect that
            if (getAccessor.DeclaredAccessibility < propAccessibility)
            {
                return ConvertStatus.Override;
            }

            if (containingType.TypeKind == TypeKind.Struct && !containingType.IsReadOnly)
            {
                // in a struct, our default is to have a public set
                // but anything else we can still convert and override
                if (setAccessor == null ||
                    // if the user had their property as internal then we are fine with completely moving
                    // an internal (by default) set method, but if they explicitly mark the set as internal
                    // while the property is public we want to keep that behavior
                    (setAccessor.DeclaredAccessibility != Accessibility.Public &&
                        setAccessor.DeclaredAccessibility != propAccessibility) ||
                    setAccessor.IsInitOnly)
                {
                    return ConvertStatus.Override;
                }
            }
            else
            {
                // either we are a class or readonly struct, the default is no set or init only set
                if (setAccessor != null)
                {
                    if (setAccessor.DeclaredAccessibility != Accessibility.Public &&
                        setAccessor.DeclaredAccessibility != propAccessibility)
                    {
                        return ConvertStatus.Override;
                    }

                    if (!setAccessor.IsInitOnly)
                    {
                        return ConvertStatus.OverrideIfConvertingSetToInit;
                    }
                }
            }

            return ConvertStatus.AlwaysConvert;
        }
    }
}
