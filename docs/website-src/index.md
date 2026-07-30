<div class="js-home">
  <section class="js-home-intro">
    <img class="js-home-logo" src="images/jsonsettings.icon.png" alt="Nucs.JsonSettings logo">
    <p class="js-home-kicker">Settings for .NET, in one line</p>
    <h1>Nucs.JsonSettings</h1>
    <p class="js-home-lede">
      The easiest way you'll ever write settings for your app. Cross-platform, modular,
      and still a one-liner &mdash; built on Json.NET, so nested objects, dictionaries and
      lists just serialize, with no mapping to write.
    </p>
    <div class="js-home-actions" aria-label="Primary documentation links">
      <a class="js-home-button js-home-button-primary" href="docs/intro.md">Get started</a>
      <a class="js-home-button" href="docs/the-basics.md">The basics</a>
      <a class="js-home-button" href="api/index.md">API reference</a>
    </div>
  </section>

  <section class="js-home-code" aria-label="Quick start example">
    <div class="js-home-code-head">
      <h2>Install and run</h2>
      <p>Inherit one class, load with one call, change a property, save.</p>
    </div>
    <div class="js-code-grid">
      <pre><code class="lang-bash">dotnet add package Nucs.JsonSettings
dotnet add package Nucs.JsonSettings.Autosave</code></pre>
      <pre><code class="lang-csharp">using Nucs.JsonSettings;

class MySettings : JsonSettings {
    public override string FileName { get; set; } = "config.json";
    public string Name { get; set; } = "default";
}

var settings = JsonSettings.Load&lt;MySettings&gt;("config.json");
settings.Name = "ok";
settings.Save();</code></pre>
    </div>
  </section>

  <section class="js-home-stats" aria-label="Project highlights">
    <div>
      <strong>5 targets</strong>
      <span>netstandard2.0, net48, net6/8/10</span>
    </div>
    <div>
      <strong>AES-256</strong>
      <span>Password encryption module</span>
    </div>
    <div>
      <strong>Autosave</strong>
      <span>Persist on property change</span>
    </div>
    <div>
      <strong>Modular</strong>
      <span>Per-object load/save pipeline</span>
    </div>
  </section>

  <section class="js-home-section">
    <div class="js-home-section-head">
      <h2>Documentation Map</h2>
      <p>Pick what you're reaching for.</p>
    </div>
    <div class="js-card-grid">
      <a class="js-doc-card" href="docs/the-basics.md">
        <span>Start here</span>
        <strong>The Basics</strong>
        <p>Hardcoded POCO settings, dynamic bags, encryption and autosave &mdash; the four ways to load.</p>
      </a>
      <a class="js-doc-card" href="docs/dynamic-settings-bag.md">
        <span>Dynamic</span>
        <strong>SettingsBag</strong>
        <p>A key/value bag when you don't want to hardcode a settings class.</p>
      </a>
      <a class="js-doc-card" href="docs/encryption.md">
        <span>Security</span>
        <strong>Encryption</strong>
        <p>AES-256 over the serialized JSON, keyed by a password or a password fetcher.</p>
      </a>
      <a class="js-doc-card" href="docs/autosave.md">
        <span>Persistence</span>
        <strong>Autosave</strong>
        <p>Save automatically on change, with WPF <code>INotifyPropertyChanged</code> support and suspension.</p>
      </a>
      <a class="js-doc-card" href="docs/recovery.md">
        <span>Resilience</span>
        <strong>Recovery &amp; Versioning</strong>
        <p>Decide what happens when a file fails to parse or its schema version moves on.</p>
      </a>
      <a class="js-doc-card" href="docs/modulation-api.md">
        <span>Extend</span>
        <strong>Modulation API</strong>
        <p>The load/save event pipeline every feature is built on &mdash; write your own module.</p>
      </a>
    </div>
  </section>

  <section class="js-home-section">
    <div class="js-home-section-head">
      <h2>What JsonSettings Optimizes For</h2>
      <p>One-liner ergonomics, with full control when you need it.</p>
    </div>
    <div class="js-feature-list">
      <div>
        <strong>No mapping, ever</strong>
        <p>It is built around Json.NET, so nested custom objects, dictionaries and lists serialize out of the box, with every Json.NET attribute and setting available to you.</p>
      </div>
      <div>
        <strong>Modular by design</strong>
        <p>Encryption, Base64, versioning, recovery and autosave are all modules on a per-object socket. Attach only what an object needs.</p>
      </div>
      <div>
        <strong>Cross-platform</strong>
        <p>The same package runs on .NET Framework, modern .NET, Unity and Xamarin through its netstandard2.0 asset.</p>
      </div>
      <div>
        <strong>Hardcoded or dynamic</strong>
        <p>Inherit <code>JsonSettings</code> for a typed POCO, or use <code>SettingsBag</code> for a dynamic key/value store &mdash; both save the same way.</p>
      </div>
    </div>
  </section>

  <section class="js-home-links" aria-label="Community links">
    <a href="https://github.com/Nucs/JsonSettings">GitHub Repository</a>
    <a href="https://www.nuget.org/packages/Nucs.JsonSettings">NuGet Package</a>
    <a href="https://www.nuget.org/packages/Nucs.JsonSettings.Autosave">Autosave Package</a>
  </section>
</div>
