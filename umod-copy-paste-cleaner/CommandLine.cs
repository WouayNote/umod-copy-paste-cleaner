using CommandLine;
using CommandLine.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WouayNote.UModeCopyPasteCleaner {

  class CommandLine {

    private const int SupportedJsonSettingsVersion = 1;
    private const int SupportedJsonDataMajor = 4;
    private static readonly string ThisAppFilePath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
    private static readonly string SettingsFilePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisAppFilePath) + ".\\", Path.GetFileNameWithoutExtension(ThisAppFilePath) + ".json"));

    public class Settings {
      [JsonProperty(PropertyName="version")]
      public int version = SupportedJsonSettingsVersion;
      [JsonProperty(PropertyName="filters")]
      public List<Filter> filters = new();

      public class Filter {
        [JsonProperty(PropertyName="filter-id")]
        public string filterId = "";
        [JsonProperty(PropertyName="removed-prefabs")]
        public List<string> removePrefabs = new();
        [JsonProperty(PropertyName="switchedoff-prefabs")]
        public List<string> switchOffPrefabs = new();
      }
    }

    [Verb("get-info", HelpText = "Get information about a copied base.")]
    public class ArgumentsGetInfo {

      [Usage]
      public static IEnumerable<Example> Examples {
        get {
          return new List<Example>() {
            new Example("Display information about a copied base", new ArgumentsGetInfo { InputPath = "c:\\copied-base.json" })
          };
        }
      }

      [Option("input", Required = true, HelpText = "The file containing data that aims to be queried.")]
      public string? InputPath { get; set; }

    }

    private const string InitSettingsVerb = "init-settings";
    [Verb(InitSettingsVerb, HelpText = "Create a settings file with samples that can be used to clean copied bases.\nThis file is created next to this program, with same name but with .json extention.\nThis file can contains different filters identified by a name and can be enriched if it is necessary.")]
    public class ArgumentsInitSettings {

      [Usage]
      public static IEnumerable<Example> Examples {
        get {
          return new List<Example>() {
            new Example("Create the settings file", new ArgumentsInitSettings())
          };
        }
      }
    }

    [Verb("do-clean", HelpText = "Clean a copied base file or a directory containing copied base files.")]
    public class ArgumentsCleanFile {

      [Usage]
      public static IEnumerable<Example> Examples {
        get {
          return new List<Example>() {
            new Example("Clean a file containing a copied base", new ArgumentsCleanFile { InputPath = "c:\\copied-base-file.json", OutputPath = "c:\\cleaned-base-file.json", FilterId = "default" }),
            new Example("Clean a directory containing copied bases files", new ArgumentsCleanFile { InputPath = "c:\\copied-bases-dir", OutputPath = "c:\\cleaned-bases-dir", FilterId = "default" }),
          };
        }
      }

      public const string InputOption = "input";
      [Option(InputOption, Required = true, HelpText = "The file or directory containing data that aims to be cleaned.\nIn case a directory is specified, --" + OutputOption + " must also be a directory.")]
      public string? InputPath { get; set; }

      public const string OutputOption = "output";
      [Option(OutputOption, Required = true, HelpText = "The file or directory to be writen with cleaned data.\nIn case a directory is specified, this directory must exist already.\nIn case --" + InputOption + " is a directory, this must also be a directory.")]
      public string? OutputPath { get; set; }

      [Option("overwrite", Required = false, HelpText = "Optional. When set, overwrite the output file if already exists.")]
      public bool Overwrite { get; set; }

      public const string FilterIdOption = "filter-id";
      [Option(FilterIdOption, Required = false, HelpText = "Optional if the settings file contains only one filter.\nThe id of the filter that must be used for removing and switching-off entities.\nCaution: the filter id is case sensitive.")]
      public string? FilterId { get; set; }

      [Option("owner-id", Required = false, HelpText = "Optional. The id of owner that must be assigned to entities.")]
      public long OwnerId { get; set; }

      [Option("lock-code", Required = false, HelpText = "Optional. The 4 digits number that must be assigned to code locks.")]
      public string? LockNewCode { get; set; }

      [Option("lock-remove", Required = false, HelpText = "Optional. When set, remove all code and key locks.")]
      public bool LockRemoveAll { get; set; }
    }

    public static int Main(string[] args) {
      //parsing command line arguments
      return Parser.Default.ParseArguments<ArgumentsGetInfo, ArgumentsInitSettings, ArgumentsCleanFile>(args).MapResult(
        (ArgumentsGetInfo arguments) => ProcessInfo(arguments),
        (ArgumentsInitSettings arguments) => ProcessInstall(arguments),
        (ArgumentsCleanFile arguments) => ProcessClean(arguments),
        errors => 1
      );
    }

    private static int ProcessInfo(ArgumentsGetInfo arguments) {
      //check input file exists
      arguments.InputPath = arguments.InputPath == null ? "" : Path.GetFullPath(arguments.InputPath);
      if (!File.Exists(arguments.InputPath)) {
        Console.Out.WriteLine("Operation aborted as input file can not be found: '" + arguments.InputPath + "'.");
        return 1;
      }
      //read data from input file
      Console.Out.Write("Start loading '" + arguments.InputPath + "'... ");
      JObject data;
      try {
        data = JObject.Parse(File.ReadAllText(arguments.InputPath));
      }
      catch (Exception ex) {
        Console.Out.WriteLine("failed.");
        Console.Out.WriteLine("Not a valid json file. " + ex.Message);
        return 1;
      }
      Console.Out.WriteLine("done.");
      JToken? majorVersion = data.SelectToken("protocol.version.Major", false);
      if (majorVersion == null) {
        Console.Out.WriteLine("Operation aborted as input file is not a copied base.");
        return 1;
      }
      if (majorVersion.Value<int>() != SupportedJsonDataMajor) {
        Console.Out.WriteLine("Operation aborted as input file major version is not supported.");
        Console.Out.WriteLine("Expected major is '" + SupportedJsonDataMajor + "' whereas current major is " + majorVersion.ToString() + ".");
        return 1;
      }
      //count entities per owner
      Console.Out.WriteLine("\n[Owners]");
      Dictionary<long, int> countPerOwners = GetPrefabsCountPerOwnerId(data);
      int biggestOwnerChars = countPerOwners.Keys.Select(owner => owner.ToString().Count()).Max();
      int biggestOwnerCountChars = countPerOwners.Values.Max().ToString().Count();
      foreach (KeyValuePair<long, int> countPerOwner in countPerOwners.OrderByDescending(id => id.Value)) {
        Console.Out.WriteLine("Player " + countPerOwner.Key.ToString().PadLeft(biggestOwnerChars) + " owns " + countPerOwner.Value.ToString().PadLeft(biggestOwnerCountChars) + " prefabs");
      }
      //code locks
      Console.Out.WriteLine("\n[Locks]");
      int keyLocksCount = data.SelectTokens("entities[*].lock", false).Where(l => l["code"] == null).Count();
      Dictionary<string, int> countPerCodeLocks = data.SelectTokens("entities[*].lock.code", false).ToList().GroupBy(c => c.Value<string?>() ?? "").ToDictionary(g => g.Key, g => g.Count());
      int biggestCodeLockCountChars = countPerCodeLocks.Values.Concat(Enumerable.Repeat(keyLocksCount, 1)).Max().ToString().Count();
      Console.Out.WriteLine(keyLocksCount.ToString().PadLeft(biggestCodeLockCountChars) + " lock(s) with key");
      foreach (KeyValuePair<string, int> countPerCodeLock in countPerCodeLocks.OrderByDescending(code => code.Value)) {
        Console.Out.WriteLine(countPerCodeLock.Value.ToString().PadLeft(biggestCodeLockCountChars) + " lock(s) with code " + countPerCodeLock.Key);
      }
      //count entities per prefabs
      Console.Out.WriteLine("\n[Prefabs]");
      Dictionary<string, int> countPerPrefabs = data.SelectTokens("entities[*].prefabname", false).GroupBy(name => name.Value<string?>() ?? "").ToDictionary(g => g.Key, g => g.Count());
      int biggestPrefabCountChars = countPerPrefabs.Values.Max().ToString().Count();
      foreach (KeyValuePair<string, int> countPerPrefab in countPerPrefabs.OrderBy(name => name.Key)) {
        Console.Out.WriteLine(countPerPrefab.Value.ToString().PadLeft(biggestPrefabCountChars) + " > " + countPerPrefab.Key);
      }
      return 0;
    }

    private static int ProcessInstall(ArgumentsInitSettings arguments) {
      //check output file does not exist if no force option
      if (File.Exists(SettingsFilePath)) {
        Console.Out.WriteLine("Operation aborted as a settings file already exists: '" + SettingsFilePath + "'.");
        return 1;
      }
      if (Directory.Exists(SettingsFilePath)) {
        Console.Out.WriteLine("Operation aborted as settings file path is an existing directory: '" + SettingsFilePath + "'.");
        return 1;
      }
      //write data to output file
      Console.Out.Write("Start writing settings file '" + SettingsFilePath + "'... ");
      try {
        using (StreamWriter streamWriter = File.CreateText(SettingsFilePath))
        using (JsonTextWriter jsonWriter = new (streamWriter)) {
          new JsonSerializer { Formatting = Formatting.Indented }.Serialize(jsonWriter, new Settings() {
            filters = new() {
              new() { filterId = "clean-nothing" },
              new() {
                filterId = "default-clean",
                removePrefabs = new string[] {
                  "assets/prefabs/deployable/bed/*",
                  "assets/prefabs/deployable/elevator/*",
                  "assets/prefabs/deployable/playerioents/industrialadaptors/*",
                  "assets/prefabs/deployable/playerioents/industrialcrafter/*",
                  "assets/prefabs/deployable/sleeping bag/*",
                  "assets/content/vehicles/modularcar/module_entities/*"
                }.ToList(),
                switchOffPrefabs = new string[] {
                  "assets/prefabs/deployable/playerioents/industrialconveyor/industrialconveyor.deployed.prefab",
                  "assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab"
                }.ToList()
              }
            }
          });
        }
      }
      catch (Exception ex) {
        Console.Out.WriteLine("failed.");
        Console.Out.WriteLine(ex.Message);
        return 1;
      }
      Console.Out.WriteLine("done.");
      return 0;
    }

    private static int ProcessClean(ArgumentsCleanFile arguments) {
      //check settings file
      if (!File.Exists(SettingsFilePath)) {
        Console.Out.WriteLine("Operation aborted as settings file can not be found: '" + SettingsFilePath + "'.");
        Console.Out.WriteLine("A sample file can be created by executing following command: " + Path.GetFileNameWithoutExtension(ThisAppFilePath) + " " + InitSettingsVerb);
        return 1;
      }
      //load settings schema
      String settingsJsonSchema;
      using (Stream? stream = Assembly.GetExecutingAssembly()?.GetManifestResourceStream("WouayNote.UModeCopyPasteCleaner.settings-schema.json"))
      using (StreamReader? reader = stream == null ? null : new StreamReader(stream)) {
        if (reader == null) {
          throw new Exception("SEVERE: Unable to get schema settings resource file. Contact the programmer.");
        }
        settingsJsonSchema = reader.ReadToEnd();
      }
      //load settings
      Console.Out.Write("Start loading '" + SettingsFilePath + "'... ");
      Settings settings;
      try {
        JObject settingsJson = JObject.Parse(File.ReadAllText(SettingsFilePath));
        IList<ValidationError> errors = settingsJson.IsValid(JSchema.Parse(settingsJsonSchema), out errors) || errors == null ? new List<ValidationError>() : errors;
        foreach (ValidationError error in errors) {
          throw new Exception("Error detected at position [line " + error.LineNumber + ", char " + error.LinePosition + "]: " + error.Message);
        }
        settings = settingsJson.ToObject<Settings>() ?? throw new Exception("Json settings deserializer returned null.");
      }
      catch (Exception ex) {
        Console.Out.WriteLine("failed.");
        Console.Out.WriteLine("Not a valid json file. " + ex.Message);
        Console.Out.WriteLine("A sample file can be created by executing following command: " + Path.GetFileNameWithoutExtension(ThisAppFilePath) + " " + InitSettingsVerb);
        return 1;
      }
      Console.Out.WriteLine("done.");
      //check settings content
      if (settings.version != SupportedJsonSettingsVersion) {
        Console.Out.WriteLine("Operation aborted as settings file version is not supported.");
        Console.Out.WriteLine("Expected version is '" + SupportedJsonSettingsVersion + "' whereas current version is '" + settings.version + "'.");
        Console.Out.WriteLine("A sample file can be created by executing following command: " + Path.GetFileNameWithoutExtension(ThisAppFilePath) + " " + InitSettingsVerb);
        return 1;
      }
      if (!settings.filters.Any()) {
        Console.Out.WriteLine("Operation aborted as settings file does not contain any filter: '" + SettingsFilePath + "'.");
        Console.Out.WriteLine("A sample file can be created by executing following command: " + Path.GetFileNameWithoutExtension(ThisAppFilePath) + " " + InitSettingsVerb);
        return 1;
      }
      IEnumerable<String> duplicateFilters = settings.filters.Select(filter => filter.filterId).GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key);
      if (duplicateFilters.Any()) {
        Console.Out.WriteLine("Operation aborted as settings file contains duplicate filter ids: '" + SettingsFilePath + "'.");
        Console.Out.WriteLine("Here is the list of duplicated filter ids: '" + String.Join("', '", duplicateFilters) + "'.");
        Console.Out.WriteLine("A sample file can be created by executing following command: " + Path.GetFileNameWithoutExtension(ThisAppFilePath) + " " + InitSettingsVerb);
        return 1;
      }
      //check input
      arguments.InputPath = arguments.InputPath == null ? "" : Path.GetFullPath(arguments.InputPath);
      bool inputIsFile = File.Exists(arguments.InputPath);
      if (!inputIsFile && !Directory.Exists(arguments.InputPath)) {
        Console.Out.WriteLine("Operation aborted as input can not be found: '" + arguments.InputPath + "'.");
        return 1;
      }
      //check output
      arguments.OutputPath = arguments.OutputPath == null ? "" : Path.GetFullPath(arguments.OutputPath);
      bool outputIsFold = Directory.Exists(arguments.OutputPath);
      //convert input to list of files
      Dictionary<string, string> ioPathMapping;
      if (inputIsFile && !outputIsFold) {
        ioPathMapping = new Dictionary<string, string> { { arguments.InputPath, arguments.OutputPath } };
      }
      else if (inputIsFile && outputIsFold) {
        ioPathMapping = new Dictionary<string, string> { { arguments.InputPath, Path.Combine(arguments.OutputPath, Path.GetFileName(arguments.InputPath)) } };
      }
      else if (!inputIsFile && !outputIsFold) {
        Console.Out.WriteLine("Operation aborted as input is a directory whereas output is an existing file or a directory that does not exist.");
        return 1;
      }
      else {
        ioPathMapping = Directory.GetFiles(arguments.InputPath, "*.json").ToDictionary(i => i, i => Path.Combine(arguments.OutputPath, Path.GetFileName(i)));
      }
      //check output does not alreay exist in case of no owverwrite
      if (!arguments.Overwrite) {
        List<string> existingOutputs = ioPathMapping.Values.Where(o => File.Exists(o)).ToList();
        if (existingOutputs.Count > 0) {
          foreach (string output in existingOutputs) {
            Console.Out.WriteLine("Operation aborted as output file already exists: '" + output + "'.");
          }
          return 1;
        }
      }
      //clean each file
      foreach (KeyValuePair<string, string> ioPath in ioPathMapping.OrderBy(io => io.Key)) {
        arguments.InputPath = ioPath.Key;
        arguments.OutputPath = ioPath.Value;
        if (ProcessCleanFile(arguments, settings) != 0) {
          return 1;
        }
      }
      return 0;
    }

    private static int ProcessCleanFile(ArgumentsCleanFile arguments, Settings settings) {
      //check input file exists
      arguments.InputPath = arguments.InputPath == null ? "" : Path.GetFullPath(arguments.InputPath);
      if (!File.Exists(arguments.InputPath)) {
        Console.Out.WriteLine("Operation aborted as input file can not be found: '" + arguments.InputPath + "'.");
        return 1;
      }
      //check output file does not exist if no force option
      arguments.OutputPath = arguments.OutputPath == null ? "" : Path.GetFullPath(arguments.OutputPath);
      if (!arguments.Overwrite && File.Exists(arguments.OutputPath)) {
        Console.Out.WriteLine("Operation aborted as output file already exists: '" + arguments.OutputPath + "'.");
        return 1;
      }
      //check setting file exists
      arguments.FilterId = arguments.FilterId == null && settings.filters.Count == 1 ? settings.filters.First().filterId : arguments.FilterId;
      if (arguments.FilterId == null || !settings.filters.Where( filter => filter.filterId.Equals(arguments.FilterId)).Any()) {
        if (arguments.FilterId == null) {
          Console.Out.WriteLine("Operation aborted as there are several filters available whereas no --" + ArgumentsCleanFile.FilterIdOption + " option has been specified.");
        }
        else {
          Console.Out.WriteLine("Operation aborted as filter id '" + arguments.FilterId + "' can not be found in: '" + SettingsFilePath + "'.");
        }
        Console.Out.WriteLine("Here is the list of filters available: '" + String.Join("', '", settings.filters.Select(filter => filter.filterId)) + "'.");
        Console.Out.WriteLine("As a reminder, the filter id is case sensitive.");
        return 1;
      }
      //check code lock to be set is 4 digits
      if (arguments.LockNewCode != null && !Regex.IsMatch(arguments.LockNewCode, @"^\d\d\d\d$")) {
        Console.Out.WriteLine("Operation aborted as code lock is not a 4 digits number.");
        return 1;
      }
      //read data from input file
      Console.Out.Write("Start loading '" + arguments.InputPath + "'... ");
      JObject data;
      try {
        data = JObject.Parse(File.ReadAllText(arguments.InputPath));
      }
      catch (Exception ex) {
        Console.Out.WriteLine("failed.");
        Console.Out.WriteLine("Not a valid json file. " + ex.Message);
        return 1;
      }
      Console.Out.WriteLine("done.");
      //check major revision of input
      JToken? majorVersion = data.SelectToken("protocol.version.Major", false);
      if (majorVersion == null) {
        Console.Out.WriteLine("Operation aborted as input file is not a copied base.");
        return 1;
      }
      if (majorVersion.Value<int>() != SupportedJsonDataMajor) {
        Console.Out.WriteLine("Operation aborted as input file major version is not supported.");
        Console.Out.WriteLine("Expected major is '" + SupportedJsonDataMajor + "' whereas current major is " + majorVersion.ToString() + ".");
        return 1;
      }
      //filter nodes
      Settings.Filter filter = settings.filters.Where(filter => filter.filterId.Equals(arguments.FilterId)).First();
      if (filter != null) {
        //remove prefabs
        if (filter.removePrefabs.Any()) {
          Console.Out.Write("Start removing prefabs entities... ");
          data["entities"]?.Where(e => PrefabMatchWith(e.Value<string>("prefabname"), filter.removePrefabs)).ToList().ForEach(e => e.Remove());
          Console.Out.WriteLine("done.");
        }
        //switch off prefabs
        if (filter.switchOffPrefabs.Any()) {
          Console.Out.Write("Start switching off prefabs entities... ");
          data["entities"]?.Where(e => PrefabMatchWith(e.Value<string>("prefabname"), filter.switchOffPrefabs)).Select(e => e.SelectToken("flags.On", false)).Where(v => v != null && v.Value<bool>()).ToList().ForEach(v => v?.Parent?.Remove());
          Console.Out.WriteLine("done.");
        }
      }
      //fix prefabs without owner
      if (arguments.OwnerId == 0) {
        Console.Out.Write("Start assigning the biggest builder to entities without owner... ");
        long defaultOwnerId = GetPrefabsCountPerOwnerId(data).OrderBy(p => p.Value).Last().Key;
        data["entities"]?.Where(e => e["ownerid"] != null && e.Value<long>("ownerid") == 0).ToList().ForEach(e => e["ownerid"] = defaultOwnerId);
        Console.Out.WriteLine("done.");
      }
      //assign ownership
      else {
        Console.Out.Write("Start assigning ownership to entities... ");
        data["entities"]?.Where(e => e["ownerid"] != null).ToList().ForEach(e => e["ownerid"] = arguments.OwnerId);
        Console.Out.WriteLine("done.");
      }
      //assign code locks
      if (arguments.LockNewCode != null) {
        Console.Out.Write("Start changing all code locks number... ");
        if (arguments.LockRemoveAll) {
          Console.Out.WriteLine("skipped.");
        }
        else {
          data.SelectTokens("entities[*].lock", false).Where(l => l["code"] != null).ToList().ForEach(l => l["code"] = arguments.LockNewCode);
          Console.Out.WriteLine("done.");
        }
      }
      //remove all locks
      if (arguments.LockRemoveAll) {
        Console.Out.Write("Start removing all code and key locks... ");
        data.SelectTokens("entities[*].lock", false).ToList().ForEach(l => l.Parent?.Remove());
        Console.Out.WriteLine("done.");
      }
      //write data to temp file
      string tempFilePath = Path.GetTempFileName();
      Console.Out.Write("Start writing to temporary file '" + tempFilePath + "'... ");
      try {
        using (StreamWriter streamWriter = File.CreateText(tempFilePath))
        using (JsonTextWriter jsonWriter = new (streamWriter)) {
          jsonWriter.Formatting = Formatting.Indented;
          data.WriteTo(jsonWriter);
        }
      }
      catch (Exception ex) {
        Console.Out.WriteLine("failed.");
        Console.Out.WriteLine(ex.Message);
        return 1;
      }
      Console.Out.WriteLine("done.");
      //move temp file to output file
      if (File.Exists(arguments.OutputPath)) {
        Console.Out.Write("Start deleting old output file '" + arguments.OutputPath + "'... ");
        try {
          File.Delete(arguments.OutputPath);
        }
        catch (Exception ex) {
          Console.Out.WriteLine("failed.");
          Console.Out.WriteLine(ex.Message);
          return 1;
        }
        Console.Out.WriteLine("done.");
      }
      Console.Out.Write("Start moving temporary file to '" + arguments.OutputPath + "'... ");
      try {
        File.Move(tempFilePath, arguments.OutputPath);
      }
      catch (Exception ex) {
        Console.Out.WriteLine("failed.");
        Console.Out.WriteLine(ex.Message);
        return 1;
      }
      Console.Out.WriteLine("done.");
      return 0;
    }

    private static bool PrefabMatchWith(string? prefab, List<string> patterns) {
      return prefab != null && patterns.Where(pattern => pattern.Equals(prefab) || (pattern.EndsWith("/*") && prefab.StartsWith(pattern.Substring(0, pattern.Length - 1)))).Any();
    }

    private static Dictionary<long, int> GetPrefabsCountPerOwnerId(JObject data) {
      return data.SelectTokens("entities[*].ownerid", false).GroupBy(id => id.Value<long>()).ToDictionary(g => g.Key, g => g.Count());
    }
  }
}
