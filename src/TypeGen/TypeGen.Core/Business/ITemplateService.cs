﻿namespace TypeGen.Core.Business
{
    internal interface ITemplateService
    {
        GeneratorOptions GeneratorOptions { get; set; }
        string FillClassTemplate(string imports, string name, string extends, string properties, string customHead, string customBody);
        string FillClassPropertyWithDefaultValueTemplate(string accessor, string name, string type, string defaultValue);
        string FillClassPropertyTemplate(string accessor, string name, string type);
        string FillInterfaceTemplate(string imports, string name, string extends, string properties, string customHead, string customBody);
        string FillInterfacePropertyTemplate(string name, string type, bool isOptional);
        string FillEnumTemplate(string imports, string name, string values, bool isConst);
        string FillEnumValueTemplate(string name, int intValue);
        string FillImportTemplate(string name, string typeAlias, string path);
        string FillIndexTemplate(string exports);
        string FillIndexExportTemplate(string filename);
        string GetExtendsText(string name);
    }
}