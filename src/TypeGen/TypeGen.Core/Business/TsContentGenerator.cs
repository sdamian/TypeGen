﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TypeGen.Core.Converters;
using TypeGen.Core.Extensions;
using TypeGen.Core.Storage;
using TypeGen.Core.TypeAnnotations;
using TypeGen.Core.Utils;
using TypeGen.Core.Validation;

namespace TypeGen.Core.Business
{
    /// <summary>
    /// Contains logic for generating TypeScript file contents
    /// </summary>
    internal class TsContentGenerator : ITsContentGenerator
    {
        private readonly ITypeDependencyService _typeDependencyService;
        private readonly ITypeService _typeService;
        private readonly ITemplateService _templateService;
        private readonly ITsContentParser _tsContentParser;

        private const string KeepTsTagName = "keep-ts";
        private const string CustomHeadTagName = "custom-head";
        private const string CustomBodyTagName = "custom-body";

        public TsContentGenerator(ITypeDependencyService typeDependencyService,
            ITypeService typeService,
            ITemplateService templateService,
            ITsContentParser tsContentParser)
        {
            _typeDependencyService = typeDependencyService;
            _typeService = typeService;
            _templateService = templateService;
            _tsContentParser = tsContentParser;
        }

        /// <summary>
        /// Gets code for the 'imports' section for a given type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir">ExportTs... attribute's output dir</param>
        /// <param name="fileNameConverters"></param>
        /// <param name="typeNameConverters"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when one of: type, fileNameConverters or typeNameConverters is null</exception>
        public string GetImportsText(Type type, string outputDir, TypeNameConverterCollection fileNameConverters, TypeNameConverterCollection typeNameConverters)
        {
            Requires.NotNull(type, nameof(type));
            Requires.NotNull(fileNameConverters, nameof(fileNameConverters));
            Requires.NotNull(typeNameConverters, nameof(typeNameConverters));

            string result = GetTypeDependencyImportsText(type, outputDir, fileNameConverters, typeNameConverters);
            result += GetCustomImportsText(type);

            if (!string.IsNullOrEmpty(result))
            {
                result += "\r\n";
            }

            return result;
        }

        /// <summary>
        /// Gets the text for the "extends" section
        /// </summary>
        /// <param name="type"></param>
        /// <param name="typeNameConverters"></param>
        /// <returns></returns>
        public string GetExtendsText(Type type, TypeNameConverterCollection typeNameConverters)
        {
            Requires.NotNull(type, nameof(type));
            Requires.NotNull(typeNameConverters, nameof(typeNameConverters));
            
            Type baseType = _typeService.GetBaseType(type);
            if (baseType == null) return "";

            string baseTypeName = _typeService.GetTsTypeName(baseType, typeNameConverters, true);
            return _templateService.GetExtendsText(baseTypeName);
        }

        /// <summary>
        /// Returns TypeScript imports source code related to type dependencies
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <param name="fileNameConverters"></param>
        /// <param name="typeNameConverters"></param>
        /// <returns></returns>
        private string GetTypeDependencyImportsText(Type type, string outputDir, TypeNameConverterCollection fileNameConverters, TypeNameConverterCollection typeNameConverters)
        {
            if (!string.IsNullOrEmpty(outputDir) && !outputDir.EndsWith("/") && !outputDir.EndsWith("\\")) outputDir += "\\";
            var result = "";
            IEnumerable<TypeDependencyInfo> typeDependencies = _typeDependencyService.GetTypeDependencies(type);

            // exclude base type dependency if TsCustomBaseAttribute is specified (it will be added in custom imports)
            if (type.GetTypeInfo().GetCustomAttribute<TsCustomBaseAttribute>() != null)
            {
                typeDependencies = typeDependencies.Where(td => !td.IsBase);
            }

            foreach (TypeDependencyInfo typeDependencyInfo in typeDependencies)
            {
                Type typeDependency = typeDependencyInfo.Type;

                string dependencyOutputDir = GetTypeDependencyOutputDir(typeDependencyInfo, outputDir);

                // get path diff
                string pathDiff = FileSystemUtils.GetPathDiff(outputDir, dependencyOutputDir);
                pathDiff = pathDiff.StartsWith("..\\") || pathDiff.StartsWith("../") ? pathDiff : $"./{pathDiff}";

                // get type & file name
                string typeDependencyName = typeDependency.Name.RemoveTypeArity();
                string fileName = fileNameConverters.Convert(typeDependencyName, typeDependency);

                // get file path
                string dependencyPath = Path.Combine(pathDiff, fileName);
                dependencyPath = dependencyPath.Replace('\\', '/');

                string typeName = typeNameConverters.Convert(typeDependencyName, typeDependency);
                result += _templateService.FillImportTemplate(typeName, "", dependencyPath);
            }

            return result;
        }

        /// <summary>
        /// Gets code for imports that are specified in TsTypeAttribute.ImportPath or TsCustomBaseAttribute.ImportPath properties
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private string GetCustomImportsText(Type type)
        {
            var resultLines = new List<string>();

            resultLines.AddRange(GetCustomImportsFromCustomBase(type));
            resultLines.AddRange(GetCustomImportsFromMembers(type));

            return string.Join("", resultLines.Distinct());
        }

        private IEnumerable<string> GetCustomImportsFromMembers(Type type)
        {
            IEnumerable<MemberInfo> members = _typeService.GetTsExportableMembers(type);

            IEnumerable<TsTypeAttribute> typeAttributes = members
                .Select(memberInfo => memberInfo.GetCustomAttribute<TsTypeAttribute>())
                .Where(tsTypeAttribute => !string.IsNullOrEmpty(tsTypeAttribute?.ImportPath))
                .Distinct(new TsTypeAttributeComparer());

            foreach (TsTypeAttribute attribute in typeAttributes)
            {
                yield return FillCustomImportTemplate(attribute.FlatTypeName, attribute.ImportPath, attribute.OriginalTypeName);
            }
        }

        private IEnumerable<string> GetCustomImportsFromCustomBase(Type type)
        {
            var tsCustomBaseAttribute = type.GetTypeInfo().GetCustomAttribute<TsCustomBaseAttribute>();
            if (tsCustomBaseAttribute == null || string.IsNullOrEmpty(tsCustomBaseAttribute.ImportPath)) yield break;

            yield return FillCustomImportTemplate(tsCustomBaseAttribute.Base, tsCustomBaseAttribute.ImportPath, tsCustomBaseAttribute.OriginalTypeName);
        }

        private string FillCustomImportTemplate(string typeName, string importPath, string originalTypeName)
        {
            bool withOriginalTypeName = !string.IsNullOrEmpty(originalTypeName);

            string name = withOriginalTypeName ? originalTypeName : typeName;
            string typeAlias = withOriginalTypeName ? typeName : null;
            return _templateService.FillImportTemplate(name, typeAlias, importPath);
        }

        /// <summary>
        /// Gets the output directory for a type dependency
        /// </summary>
        /// <param name="typeDependencyInfo"></param>
        /// <param name="parentTypeOutputDir"></param>
        /// <returns></returns>
        private string GetTypeDependencyOutputDir(TypeDependencyInfo typeDependencyInfo, string parentTypeOutputDir)
        {
            var classAttribute = typeDependencyInfo.Type.GetTypeInfo().GetCustomAttribute<ExportTsClassAttribute>();
            var interfaceAttribute = typeDependencyInfo.Type.GetTypeInfo().GetCustomAttribute<ExportTsInterfaceAttribute>();
            var enumAttribute = typeDependencyInfo.Type.GetTypeInfo().GetCustomAttribute<ExportTsEnumAttribute>();

            if (classAttribute == null && enumAttribute == null && interfaceAttribute == null)
            {
                TsDefaultTypeOutputAttribute defaultTypeOutputAttribute = typeDependencyInfo.MemberAttributes
                    ?.SingleOrDefault(a => a.GetType() == typeof(TsDefaultTypeOutputAttribute))
                    as TsDefaultTypeOutputAttribute;

                return defaultTypeOutputAttribute?.OutputDir ?? parentTypeOutputDir;
            }

            return classAttribute?.OutputDir
                    ?? interfaceAttribute?.OutputDir
                    ?? enumAttribute?.OutputDir;
        }

        /// <summary>
        /// Gets custom code for a TypeScript file given by filePath.
        /// Returns an empty string if a file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="indentSize"></param>
        /// <returns></returns>
        public string GetCustomBody(string filePath, int indentSize)
        {
            Requires.NotNull(filePath, nameof(filePath));
            
            string content = _tsContentParser.GetTagContent(filePath, indentSize, KeepTsTagName, CustomBodyTagName);
            string tab = StringUtils.GetTabText(indentSize);

            return string.IsNullOrEmpty(content)
                ? ""
                : $"\r\n\r\n{tab}//<{CustomBodyTagName}>\r\n{tab}{content}{tab}//</{CustomBodyTagName}>";
        }

        /// <summary>
        /// Gets custom code for a TypeScript file given by filePath.
        /// Returns an empty string if a file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public string GetCustomHead(string filePath)
        {
            Requires.NotNull(filePath, nameof(filePath));
            
            string content = _tsContentParser.GetTagContent(filePath, 0, CustomHeadTagName);
            return string.IsNullOrEmpty(content)
                ? ""
                : $"//<{CustomHeadTagName}>\r\n{content}//</{CustomHeadTagName}>\r\n\r\n";
        }
    }
}
