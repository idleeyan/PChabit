using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public partial class SettingsService
{
    public bool AiInsightsEnabled
    {
        get => _settings.AiInsightsEnabled;
        set
        {
            if (_settings.AiInsightsEnabled != value)
            {
                _settings.AiInsightsEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiInsightsEnabled) });
            }
        }
    }

    public string AiBaseUrl
    {
        get => _settings.AiBaseUrl;
        set
        {
            if (_settings.AiBaseUrl != value)
            {
                _settings.AiBaseUrl = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiBaseUrl) });
            }
        }
    }

    public string AiApiKey
    {
        get => _settings.AiApiKey;
        set
        {
            if (_settings.AiApiKey != value)
            {
                _settings.AiApiKey = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiApiKey) });
            }
        }
    }

    public string AiModel
    {
        get => _settings.AiModel;
        set
        {
            if (_settings.AiModel != value)
            {
                _settings.AiModel = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiModel) });
            }
        }
    }

    public string AiProvider
    {
        get => _settings.AiProvider;
        set
        {
            if (_settings.AiProvider != value)
            {
                _settings.AiProvider = value ?? "zhipu";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiProvider) });
            }
        }
    }

    public bool BrowserAutoPush
    {
        get => _settings.BrowserAutoPush;
        set
        {
            if (_settings.BrowserAutoPush != value)
            {
                _settings.BrowserAutoPush = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(BrowserAutoPush) });
            }
        }
    }

    public int AiTimeoutSeconds
    {
        get => _settings.AiTimeoutSeconds;
        set
        {
            var clamped = Math.Clamp(value, 30, 900);
            if (_settings.AiTimeoutSeconds != clamped)
            {
                _settings.AiTimeoutSeconds = clamped;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiTimeoutSeconds) });
            }
        }
    }

    public bool AiStrictPrivacy
    {
        get => _settings.AiStrictPrivacy;
        set
        {
            if (_settings.AiStrictPrivacy != value)
            {
                _settings.AiStrictPrivacy = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiStrictPrivacy) });
            }
        }
    }

    public string AiModelFast
    {
        get => _settings.AiModelFast;
        set
        {
            if (_settings.AiModelFast != value)
            {
                _settings.AiModelFast = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiModelFast) });
            }
        }
    }

    public string AiLocalBaseUrl
    {
        get => _settings.AiLocalBaseUrl;
        set
        {
            if (_settings.AiLocalBaseUrl != value)
            {
                _settings.AiLocalBaseUrl = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiLocalBaseUrl) });
            }
        }
    }

    public string AiLocalModel
    {
        get => _settings.AiLocalModel;
        set
        {
            if (_settings.AiLocalModel != value)
            {
                _settings.AiLocalModel = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiLocalModel) });
            }
        }
    }

    public string AiCloudBaseUrl
    {
        get => _settings.AiCloudBaseUrl;
        set
        {
            if (_settings.AiCloudBaseUrl != value)
            {
                _settings.AiCloudBaseUrl = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiCloudBaseUrl) });
            }
        }
    }

    public string AiCloudApiKey
    {
        get => _settings.AiCloudApiKey;
        set
        {
            if (_settings.AiCloudApiKey != value)
            {
                _settings.AiCloudApiKey = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiCloudApiKey) });
            }
        }
    }

    public string AiCloudModel
    {
        get => _settings.AiCloudModel;
        set
        {
            if (_settings.AiCloudModel != value)
            {
                _settings.AiCloudModel = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiCloudModel) });
            }
        }
    }

    public string AiEndpointMode
    {
        get => _settings.AiEndpointMode;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "cloud" : value.Trim().ToLowerInvariant();
            if (v is not ("cloud" or "local" or "dual")) v = "cloud";
            if (_settings.AiEndpointMode != v)
            {
                _settings.AiEndpointMode = v;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiEndpointMode) });
            }
        }
    }

    public bool AiAutoWeeklyInsight
    {
        get => _settings.AiAutoWeeklyInsight;
        set
        {
            if (_settings.AiAutoWeeklyInsight != value)
            {
                _settings.AiAutoWeeklyInsight = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiAutoWeeklyInsight) });
            }
        }
    }
}
