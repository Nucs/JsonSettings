---
uid: Nucs.JsonSettings
summary: The primary Nucs.JsonSettings namespace — the JsonSettings base class, the dynamic SettingsBag, savable interfaces, and the library's exception types.
remarks: |
  This is where you start when writing Nucs.JsonSettings code:

  - <xref href="Nucs.JsonSettings.JsonSettings" data-throw-if-not-resolved="false"></xref> is the abstract base class you inherit for a typed, hardcoded settings object. Creation and loading go through its static API (`Load`, `Construct`, `Configure`); saving goes through the instance (`Save`).
  - <xref href="Nucs.JsonSettings.SettingsBag" data-throw-if-not-resolved="false"></xref> is a ready-made dynamic key/value settings object when you would rather not declare a class.
  - <xref href="Nucs.JsonSettings.ISavable" data-throw-if-not-resolved="false"></xref> and <xref href="Nucs.JsonSettings.IEncryptedSavable" data-throw-if-not-resolved="false"></xref> are the savable contracts.

  Optional behavior — encryption, Base64, versioning, recovery and autosave — is added per object as
  modules; see the `Nucs.JsonSettings.Modulation` namespace and the fluent extensions in
  `Nucs.JsonSettings.Fluent`.

  ```csharp
  using Nucs.JsonSettings;

  class MySettings : JsonSettings {
      public override string FileName { get; set; } = "config.json";
      public string Name { get; set; } = "default";
  }

  var settings = JsonSettings.Load<MySettings>("config.json");
  settings.Name = "ok";
  settings.Save();
  ```
---
