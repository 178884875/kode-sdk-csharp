using System.Text.Json;

namespace Kode.Agent.Sdk.Core.Skills;

/// <summary>
/// SKILL.md frontmatter metadata.
/// </summary>
public record SkillMetadata
{
    /// <summary>
    /// Skill name, required, 1-64 characters, kebab-case format.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Skill description, required, 1-1024 characters.
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// License, e.g., "Apache-2.0".
    /// </summary>
    public string? License { get; init; }
    
    /// <summary>
    /// Compatibility notes, e.g., "claude-3.5-sonnet, gpt-4".
    /// </summary>
    public string? Compatibility { get; init; }
    
    /// <summary>
    /// Allowed tools for this skill.
    /// </summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }
    
    /// <summary>
    /// Custom metadata.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// Skill resources directory.
/// </summary>
public record SkillResources
{
    /// <summary>
    /// Executable script files.
    /// </summary>
    public IReadOnlyList<string>? Scripts { get; init; }
    
    /// <summary>
    /// Reference documents.
    /// </summary>
    public IReadOnlyList<string>? References { get; init; }
    
    /// <summary>
    /// Asset files (templates, icons, etc.).
    /// </summary>
    public IReadOnlyList<string>? Assets { get; init; }
}

/// <summary>
/// Complete skill definition.
/// </summary>
public record Skill : SkillMetadata
{
    /// <summary>
    /// Skill directory absolute path.
    /// </summary>
    public required string Path { get; init; }
    
    /// <summary>
    /// SKILL.md Markdown content (loaded after activation).
    /// </summary>
    public string? Body { get; set; }
    
    /// <summary>
    /// Resources directory content.
    /// </summary>
    public SkillResources? Resources { get; init; }
    
    /// <summary>
    /// Parse timestamp.
    /// </summary>
    public long? LoadedAt { get; set; }
    
    /// <summary>
    /// Activation timestamp.
    /// </summary>
    public long? ActivatedAt { get; set; }
}

/// <summary>
/// Skills configuration.
/// </summary>
public record SkillsConfig
{
    /// <summary>
    /// Skills search paths.
    /// </summary>
    public required IReadOnlyList<string> Paths { get; init; }
    
    /// <summary>
    /// Whitelist: only load these skills.
    /// </summary>
    public IReadOnlyList<string>? Include { get; init; }
    
    /// <summary>
    /// Blacklist: exclude these skills.
    /// </summary>
    public IReadOnlyList<string>? Exclude { get; init; }
    
    /// <summary>
    /// Trusted sources: allow script execution for these skills.
    /// </summary>
    public IReadOnlyList<string>? Trusted { get; init; }
    
    /// <summary>
    /// Whether to validate format on load.
    /// </summary>
    public bool ValidateOnLoad { get; init; } = true;

    /// <summary>
    /// Skills to activate automatically at session start.
    /// Missing skill names are silently skipped.
    /// </summary>
    public IReadOnlyList<string>? AutoActivate { get; init; }
}

/// <summary>
/// Skill activation record.
/// </summary>
public record SkillActivation
{
    /// <summary>
    /// Skill name.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Activation timestamp.
    /// </summary>
    public required long ActivatedAt { get; init; }
    
    /// <summary>
    /// Activation source.
    /// </summary>
    public required SkillActivationSource ActivatedBy { get; init; }
    
    /// <summary>
    /// Granted tools.
    /// </summary>
    public IReadOnlyList<string>? ToolsGranted { get; init; }
}

/// <summary>
/// Skill activation source.
/// </summary>
public enum SkillActivationSource
{
    Auto,
    Agent,
    User
}

/// <summary>
/// Skills state for persistence.
/// </summary>
public record SkillsState
{
    public required IReadOnlyList<string> Discovered { get; init; }
    public required IReadOnlyList<SkillActivation> Activated { get; init; }
    public required long LastDiscoveryAt { get; init; }
}
