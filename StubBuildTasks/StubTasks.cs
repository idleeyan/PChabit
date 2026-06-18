using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Packaging.Pri.Tasks
{
    public class ExpandPriContent : Task
    {
        [Required]
        public ITaskItem[] Inputs { get; set; }
        public string MakePriExeFullPath { get; set; }
        public string MakePriExtensionPath { get; set; }
        public string IntermediateDirectory { get; set; }
        public string AdditionalMakepriExeParameters { get; set; }
        public bool ExcludeXamlFromLibraryLayoutsWhenXbfIsPresent { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] Expanded { get; set; }
        [Output] public ITaskItem[] IntermediateFileWrites { get; set; }

        public override bool Execute() { Expanded = new ITaskItem[0]; IntermediateFileWrites = new ITaskItem[0]; return true; }
    }

    public class CreatePriConfigXmlForSplitting : Task { public override bool Execute() => true; }
    public class CreatePriConfigXmlForMainPackageFileMap : Task { public override bool Execute() => true; }
    public class CreatePriConfigXmlForFullIndex : Task { public override bool Execute() => true; }

    public class CreatePriFilesForPortableLibraries : Task
    {
        public string MakePriExeFullPath { get; set; }
        public string MakePriExtensionPath { get; set; }
        public ITaskItem[] ContentToIndex { get; set; }
        public string IntermediateDirectory { get; set; }
        public string AdditionalMakepriExeParameters { get; set; }
        public string DefaultResourceLanguage { get; set; }
        public string DefaultResourceQualifiers { get; set; }
        public string IntermediateExtension { get; set; }
        public string TargetPlatformIdentifier { get; set; }
        public string TargetPlatformVersion { get; set; }
        public string AppxBundleAutoResourcePackageQualifiers { get; set; }
        public string SkipIntermediatePriGenerationForResourceFiles { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] IntermediateFileWrites { get; set; }
        [Output] public ITaskItem[] CreatedPriFiles { get; set; }
        [Output] public ITaskItem[] UnprocessedReswFiles_DefaultLanguage { get; set; }
        [Output] public ITaskItem[] UnprocessedReswFiles_OtherLanguages { get; set; }

        public override bool Execute()
        {
            IntermediateFileWrites = new ITaskItem[0];
            CreatedPriFiles = new ITaskItem[0];
            UnprocessedReswFiles_DefaultLanguage = new ITaskItem[0];
            UnprocessedReswFiles_OtherLanguages = new ITaskItem[0];
            return true;
        }
    }

    public class GenerateMainPriConfigurationFile : Task { public override bool Execute() => true; }

    public class GeneratePriConfigurationFiles : Task
    {
        public string UnfilteredLayoutResfilesPath { get; set; }
        public string FilteredLayoutResfilesPath { get; set; }
        public string ExcludedLayoutResfilesPath { get; set; }
        public string ResourcesResfilesPath { get; set; }
        public string PriResfilesPath { get; set; }
        public string EmbedFileResfilePath { get; set; }
        public ITaskItem[] LayoutFiles { get; set; }
        public ITaskItem[] PRIResourceFiles { get; set; }
        public ITaskItem[] PriFiles { get; set; }
        public ITaskItem[] EmbedFiles { get; set; }
        public string IntermediateExtension { get; set; }
        public ITaskItem[] UnprocessedResourceFiles_OtherLanguages { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] AdditionalResourceResFiles { get; set; }

        public override bool Execute() { AdditionalResourceResFiles = new ITaskItem[0]; return true; }
    }

    public class GenerateProjectPriFile : Task
    {
        public string MakePriExeFullPath { get; set; }
        public string MakePriExtensionPath { get; set; }
        public string PriConfigXmlPath { get; set; }
        public string IndexFilesForQualifiersCollection { get; set; }
        public string ProjectPriIndexName { get; set; }
        public string InsertReverseMap { get; set; }
        public string ProjectDirectory { get; set; }
        public string OutputFileName { get; set; }
        public string QualifiersPath { get; set; }
        public string IntermediateExtension { get; set; }
        public string AppxBundleAutoResourcePackageQualifiers { get; set; }
        public string MultipleQualifiersPerDimensionFoundPath { get; set; }
        public string AdditionalMakepriExeParameters { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] Output { get; set; }
        public override bool Execute() { Output = new ITaskItem[0]; return true; }
    }

    public class RemoveDuplicatePriFiles : Task
    {
        [Required] public ITaskItem[] Inputs { get; set; }
        public string Platform { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] Filtered { get; set; }
        public override bool Execute() { Filtered = Inputs ?? new ITaskItem[0]; return true; }
    }

    public class UpdateMainPackageFileMap : Task { public override bool Execute() => true; }
}

namespace Microsoft.Build.AppxPackage
{
    public class RemovePayloadDuplicates : Task
    {
        [Required] public ITaskItem[] Inputs { get; set; }
        public string ProjectName { get; set; }
        public string Platform { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] Filtered { get; set; }
        public override bool Execute() { Filtered = new ITaskItem[0]; return true; }
    }

    public class GetPackagingOutputs : Task { public override bool Execute() => true; }
    public class ExpandPayload : Task { public override bool Execute() => true; }

    public class ExpandPayloadDirectories : Task
    {
        [Required] public ITaskItem[] Inputs { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public ITaskItem[] Expanded { get; set; }
        public override bool Execute() { Expanded = Inputs ?? new ITaskItem[0]; return true; }
    }

    public class GetDefaultResourceLanguage : Task
    {
        public string DefaultLanguage { get; set; }
        public ITaskItem[] SourceAppxManifest { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public string DefaultResourceLanguage { get; set; }
        public override bool Execute() { DefaultResourceLanguage = DefaultLanguage ?? "en-US"; return true; }
    }

    public class GetPackageArchitecture : Task
    {
        public string Platform { get; set; }
        public ITaskItem[] ProjectArchitecture { get; set; }
        public ITaskItem[] RecursiveProjectArchitecture { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public string PackageArchitecture { get; set; }
        public override bool Execute() { PackageArchitecture = "x64"; return true; }
    }

    public class GetSdkFileFullPath : Task
    {
        public string FileName { get; set; }
        public string FullFilePath { get; set; }
        public string FileArchitecture { get; set; }
        public string RequireExeExtension { get; set; }
        public string TargetPlatformSdkRootOverride { get; set; }
        public string SDKIdentifier { get; set; }
        public string SDKVersion { get; set; }
        public string TargetPlatformIdentifier { get; set; }
        public string TargetPlatformMinVersion { get; set; }
        public string TargetPlatformVersion { get; set; }
        public string MSBuildExtensionsPath64Exists { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public string ActualFullFilePath { get; set; }
        [Output] public string ActualFileArchitecture { get; set; }

        public override bool Execute() { ActualFullFilePath = FullFilePath ?? ""; ActualFileArchitecture = FileArchitecture ?? ""; return true; }
    }

    public class GetSdkPropertyValue : Task
    {
        public string TargetPlatformSdkRootOverride { get; set; }
        public string SDKIdentifier { get; set; }
        public string SDKVersion { get; set; }
        public string TargetPlatformIdentifier { get; set; }
        public string TargetPlatformMinVersion { get; set; }
        public string TargetPlatformVersion { get; set; }
        public string PropertyName { get; set; }
        public string VsTelemetrySession { get; set; }

        [Output] public string PropertyValue { get; set; }
        public override bool Execute() { PropertyValue = ""; return true; }
    }

    public class RemoveRedundantXamlFilesFromSdkPayload : Task
    {
        [Required] public ITaskItem[] Inputs { get; set; }

        [Output] public ITaskItem[] Outputs { get; set; }
        public override bool Execute() { Outputs = Inputs ?? new ITaskItem[0]; return true; }
    }

    public class ValidateConfiguration : Task
    {
        public string TargetPlatformMinVersion { get; set; }
        public string TargetPlatformVersion { get; set; }
        public string ProjectLanguage { get; set; }
        public string VsTelemetrySession { get; set; }
        public string TargetPlatformIdentifier { get; set; }
        public string Platform { get; set; }

        public override bool Execute() => true;
    }
}
