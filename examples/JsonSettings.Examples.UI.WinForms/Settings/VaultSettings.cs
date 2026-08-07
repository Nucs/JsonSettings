using System.ComponentModel;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Examples.UI.WinForms;

/// <summary>
///     The demo's second settings object: same zero-code PropertyGrid editing, but the file on
///     disk is AES-256-CBC ciphertext — <c>WithEncryption("password")</c> in the fluent pipeline
///     below is the entire integration. The grid neither knows nor cares; encryption is a module
///     on the settings object, not a property of the UI. Press "Show raw files on disk" in the
///     form to see the difference: the profile file is readable JSON, this one is bytes.
///
///     [PasswordPropertyText] is plain System.ComponentModel: the PropertyGrid masks the value
///     while editing — pairing UI masking with at-rest encryption is the pattern real settings
///     dialogs want.
/// </summary>
[Autosave]
public sealed class VaultSettings : JsonSettings {
    public override string FileName { get; set; } = "winforms-vault.json";

    [Category("Credentials")]
    [Description("Masked in the grid by [PasswordPropertyText], encrypted on disk by the EncryptionModule. Committed edits autosave like any woven setter.")]
    [PasswordPropertyText(true)]
    public string ApiToken { get; set; } = "";

    [Category("Credentials")]
    [Description("Stored alongside the token inside the same encrypted file.")]
    public string TenantId { get; set; } = "";

    public VaultSettings() { }
    public VaultSettings(string fileName) : base(fileName) { }

    private static readonly Lazy<VaultSettings> _instance = new(LoadFromAppData);

    /// <summary>Process-wide, file-backed, autosaving, AES-encrypted settings instance.</summary>
    public static VaultSettings Instance => _instance.Value;

    private static VaultSettings LoadFromAppData() {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "JsonSettings.Examples");
        Directory.CreateDirectory(dir);
        // A literal password keeps the demo self-contained; real applications pass a
        // Func<string>/byte[] sourced from DPAPI, the OS keychain or user input. The default
        // algorithm (AES-256-CBC, PBKDF2-stretched) is used because it works on BOTH of this
        // project's targets; on net6.0+ WithEncryption also takes an EncryptionAlgorithm for
        // the AEAD ciphers (AES-GCM, AES-CCM, ChaCha20-Poly1305).
        return JsonSettings.Configure<VaultSettings>(Path.Combine(dir, "winforms-vault.json"))
                           .WithEncryption("winforms-demo-password")
                           .LoadNow()
                           .EnableAutosave();
    }
}
