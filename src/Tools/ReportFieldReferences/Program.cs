// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma warning disable RS0030
#nullable disable

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

internal sealed class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: ReportFieldReferences <path>+");
            return;
        }

        foreach (var path in args)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    RunDirectory(path);
                }
                else
                {
                    RunFile(path);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }

    static void RunDirectory(string path)
    {
        //Console.WriteLine(path);
        foreach (var filePath in Directory.GetFiles(path, "*.cs"))
        {
            RunFile(filePath);
        }
        foreach (var dirPath in Directory.GetDirectories(path))
        {
            RunDirectory(dirPath);
        }
    }

    static void RunFile(string path)
    {
        var source = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var fieldKeywords = tree.GetRoot().DescendantTokens().Where(t => t.Kind() == SyntaxKind.FieldKeyword).ToArray();
        foreach (var field in fieldKeywords)
        {
            var location = field.GetLocation();
            var text = location.SourceTree.GetText();
            var span = location.SourceSpan;
            int line = text.Lines.IndexOf(span.Start);
            var lineText = text.Lines[line];
            int column = span.Start - lineText.Start;
            Console.WriteLine("{0}: ({1}, {2}): {3}", path, line + 1, column + 1, lineText);
        }
    }
}
