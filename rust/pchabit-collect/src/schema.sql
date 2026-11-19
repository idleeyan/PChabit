CREATE TABLE AiInsightSnapshots (
                    Id TEXT PRIMARY KEY,
                    PeriodKey TEXT NOT NULL,
                    PeriodLabel TEXT NOT NULL DEFAULT '',
                    PeriodStart TEXT NOT NULL,
                    PeriodEnd TEXT NOT NULL,
                    Summary TEXT,
                    FindingsJson TEXT,
                    PlanJson TEXT,
                    DiagnosisJson TEXT,
                    RisksJson TEXT,
                    Model TEXT,
                    PromptVersion TEXT NOT NULL DEFAULT 'v2',
                    CreatedAt TEXT NOT NULL
                );
CREATE TABLE AppDailyStats (
                    Date TEXT NOT NULL,
                    ProcessName TEXT NOT NULL,
                    Minutes REAL NOT NULL DEFAULT 0,
                    Sessions INTEGER NOT NULL DEFAULT 0,
                    FocusMinutes REAL NOT NULL DEFAULT 0,
                    HourlyJson TEXT NOT NULL DEFAULT '[]',
                    LastUpdated TEXT NOT NULL,
                    PRIMARY KEY (Date, ProcessName)
                );
CREATE TABLE AppDescriptions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcessName TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    IsUserEdited INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt TEXT
                );
CREATE TABLE "AppSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppSessions" PRIMARY KEY,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NULL,
    "Duration" INTEGER NOT NULL,
    "ProcessName" TEXT NOT NULL,
    "ExecutablePath" TEXT NOT NULL,
    "AppName" TEXT NOT NULL,
    "AppVersion" TEXT NULL,
    "Publisher" TEXT NULL,
    "WindowTitle" TEXT NOT NULL,
    "WindowClass" TEXT NULL,
    "WindowX" REAL NOT NULL,
    "WindowY" REAL NOT NULL,
    "WindowWidth" REAL NOT NULL,
    "WindowHeight" REAL NOT NULL,
    "IsMaximized" INTEGER NOT NULL,
    "InputEventCount" INTEGER NOT NULL,
    "ActiveDuration" INTEGER NOT NULL,
    "ProjectContext" TEXT NULL,
    "Category" TEXT NULL
);
CREATE TABLE "ArchiveRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ArchiveRecords" PRIMARY KEY,
    "FilePath" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "FileSize" INTEGER NOT NULL,
    "DateRangeStart" TEXT NOT NULL,
    "DateRangeEnd" TEXT NOT NULL,
    "RecordCount" INTEGER NULL,
    "Description" TEXT NULL
);
CREATE TABLE "BackupRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupRecords" PRIMARY KEY,
    "FilePath" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "FileSize" INTEGER NOT NULL,
    "RecordCount" INTEGER NULL,
    "IsAutomatic" INTEGER NOT NULL,
    "Description" TEXT NULL
);
CREATE TABLE BookmarkSyncBaselines (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    SavedAt INTEGER NOT NULL DEFAULT 0,
                    ItemsJson TEXT NOT NULL DEFAULT '[]'
                );
CREATE TABLE BrowserBookmarks (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Type TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Url TEXT,
                    PathJson TEXT NOT NULL DEFAULT '[]',
                    DateAdded INTEGER NOT NULL DEFAULT 0,
                    DateModified INTEGER NOT NULL DEFAULT 0,
                    SourceBrowser TEXT NOT NULL DEFAULT '',
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt INTEGER NOT NULL DEFAULT 0
                );
CREATE TABLE BrowserHistoryItems (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Url TEXT NOT NULL,
                    Title TEXT NOT NULL DEFAULT '',
                    VisitTime INTEGER NOT NULL DEFAULT 0,
                    VisitCount INTEGER NOT NULL DEFAULT 0,
                    SourceBrowser TEXT NOT NULL DEFAULT '',
                    IngestedAt INTEGER NOT NULL DEFAULT 0
                );
CREATE TABLE BrowserSyncMetas (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );
CREATE TABLE "DailyPatterns" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DailyPatterns" PRIMARY KEY,
    "Date" TEXT NOT NULL,
    "FirstActivityTime" TEXT NOT NULL,
    "LastActivityTime" TEXT NOT NULL,
    "TotalActiveTime" TEXT NOT NULL,
    "TotalIdleTime" TEXT NOT NULL,
    "ProductivityScore" REAL NOT NULL,
    "InterruptionCount" INTEGER NOT NULL,
    "DeepWorkTime" TEXT NOT NULL
);
CREATE TABLE DailySummaries (
                    Id TEXT PRIMARY KEY,
                    Date TEXT NOT NULL,
                    TotalKeys INTEGER NOT NULL DEFAULT 0,
                    TotalMouseClicks INTEGER NOT NULL DEFAULT 0,
                    ActiveMinutes REAL NOT NULL DEFAULT 0,
                    TopApps TEXT NOT NULL DEFAULT '[]',
                    HourlyKeyDistribution TEXT NOT NULL DEFAULT '[]',
                    LastUpdated TEXT NOT NULL
                , WebPages INTEGER NOT NULL DEFAULT 0, WebDurationTicks INTEGER NOT NULL DEFAULT 0, WebActiveDurationTicks INTEGER NOT NULL DEFAULT 0, AppSwitches INTEGER, FocusMinutesV2 REAL, FocusCountV2 INTEGER, FocusQualityAvg REAL, WebMinutes REAL, CpuLoadAvg REAL, CpuLoadP95 REAL, GpuLoadAvg REAL, GpuTempMax REAL, MemLoadAvg REAL, NetBytesUp INTEGER, NetBytesDown INTEGER, MetricsVersion INTEGER, LabelMinutesJson TEXT, NightMinutes REAL, FirstActiveTime TEXT, LastActiveTime TEXT, SearchCount INTEGER);
CREATE TABLE "EfficiencyScores" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EfficiencyScores" PRIMARY KEY,
    "Date" TEXT NOT NULL,
    "Score" REAL NOT NULL,
    "FocusTimeMinutes" INTEGER NOT NULL,
    "DeepWorkMinutes" INTEGER NOT NULL,
    "InterruptionCount" INTEGER NOT NULL,
    "ProductivityRatio" REAL NOT NULL,
    "BreakRatio" REAL NOT NULL,
    "Details" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE HardwareSamples (
                    Id TEXT NOT NULL CONSTRAINT PK_HardwareSamples PRIMARY KEY,
                    Timestamp TEXT NOT NULL,
                    CpuLoadAvg REAL,
                    CpuLoadMax REAL,
                    CpuTempMax REAL,
                    GpuLoadAvg REAL,
                    GpuLoadMax REAL,
                    GpuTempMax REAL,
                    VramUsedMax REAL,
                    VramTotal REAL,
                    MemLoadAvg REAL,
                    MemLoadMax REAL,
                    MemUsedMax REAL,
                    DiskActivityAvg REAL,
                    DiskReadMax REAL,
                    DiskWriteMax REAL,
                    NetUpAvg REAL,
                    NetDownAvg REAL,
                    SampleCount INTEGER NOT NULL,
                    SensorFlags INTEGER NOT NULL DEFAULT 0
                );
CREATE TABLE "InsightReports" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_InsightReports" PRIMARY KEY,
    "ReportType" TEXT NOT NULL,
    "StartDate" TEXT NOT NULL,
    "EndDate" TEXT NOT NULL,
    "Summary" TEXT NULL,
    "Insights" TEXT NULL,
    "Recommendations" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "KeyboardSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_KeyboardSessions" PRIMARY KEY,
    "Date" TEXT NOT NULL,
    "Hour" INTEGER NOT NULL,
    "ProcessName" TEXT NULL,
    "TotalKeyPresses" INTEGER NOT NULL,
    "KeyFrequency" TEXT NOT NULL,
    "KeyCategoryFrequency" TEXT NOT NULL,
    "AverageTypingSpeed" REAL NOT NULL,
    "PeakTypingSpeed" REAL NOT NULL,
    "UndoCount" INTEGER NOT NULL,
    "DeleteCount" INTEGER NOT NULL,
    "BackspaceCount" INTEGER NOT NULL,
    "TypingBursts" TEXT NOT NULL,
    "Shortcuts" TEXT NOT NULL
, RepeatKeyPresses INTEGER NOT NULL DEFAULT 0);
CREATE TABLE "MouseSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_MouseSessions" PRIMARY KEY,
    "Date" TEXT NOT NULL,
    "Hour" INTEGER NOT NULL,
    "ProcessName" TEXT NULL,
    "LeftClickCount" INTEGER NOT NULL,
    "RightClickCount" INTEGER NOT NULL,
    "MiddleClickCount" INTEGER NOT NULL,
    "TotalMoveDistance" REAL NOT NULL,
    "MoveEventCount" INTEGER NOT NULL,
    "ScrollCount" INTEGER NOT NULL,
    "ScrollLines" INTEGER NOT NULL,
    "AverageClickInterval" REAL NOT NULL
);
CREATE TABLE NetworkTrafficSamples (
                    Id TEXT NOT NULL CONSTRAINT PK_NetworkTrafficSamples PRIMARY KEY,
                    Timestamp TEXT NOT NULL,
                    BytesUp INTEGER NOT NULL DEFAULT 0,
                    BytesDown INTEGER NOT NULL DEFAULT 0,
                    PeakUpBps REAL NOT NULL DEFAULT 0,
                    PeakDownBps REAL NOT NULL DEFAULT 0,
                    SampleCount INTEGER NOT NULL DEFAULT 0,
                    LastUpdated TEXT NOT NULL
                );
CREATE TABLE PendingBookmarkChanges (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ChangeType TEXT NOT NULL,
    Browser TEXT NOT NULL,
    ItemKey TEXT NOT NULL,
    Title TEXT NOT NULL DEFAULT '',
    Url TEXT,
    PathJson TEXT NOT NULL DEFAULT '[]',
    DetectedAt INTEGER NOT NULL DEFAULT 0,
    Status TEXT NOT NULL DEFAULT 'pending'
);
CREATE TABLE ProcessNetworkUsages (
                    Date TEXT NOT NULL,
                    Hour INTEGER NOT NULL,
                    ProcessName TEXT NOT NULL,
                    BytesUp INTEGER NOT NULL DEFAULT 0,
                    BytesDown INTEGER NOT NULL DEFAULT 0,
                    PeakBytesPerSec INTEGER NOT NULL DEFAULT 0,
                    LastUpdated TEXT NOT NULL,
                    CONSTRAINT PK_ProcessNetworkUsages PRIMARY KEY (Date, Hour, ProcessName)
                );
CREATE TABLE "ProgramCategories" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ProgramCategories" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Color" TEXT NOT NULL,
    "Icon" TEXT NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NULL
);
CREATE TABLE "ProgramCategoryMappings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ProgramCategoryMappings" PRIMARY KEY AUTOINCREMENT,
    "ProcessName" TEXT NOT NULL,
    "ProcessPath" TEXT NULL,
    "ProcessAlias" TEXT NULL,
    "CategoryId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NULL,
    CONSTRAINT "FK_ProgramCategoryMappings_ProgramCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "ProgramCategories" ("Id") ON DELETE CASCADE
);
CREATE TABLE StickyNotes (
                    Id TEXT NOT NULL CONSTRAINT PK_StickyNotes PRIMARY KEY,
                    Content TEXT NOT NULL,
                    Color TEXT NOT NULL DEFAULT 'yellow',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsPinned INTEGER NOT NULL DEFAULT 0,
                    IsArchived INTEGER NOT NULL DEFAULT 0,
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    DeletedAt TEXT NULL,
                    DeviceId TEXT NOT NULL DEFAULT '',
                    Version INTEGER NOT NULL DEFAULT 1,
                    IsOnDesktop INTEGER NOT NULL DEFAULT 0,
                    WindowLeft INTEGER NOT NULL DEFAULT -1,
                    WindowTop INTEGER NOT NULL DEFAULT -1,
                    WindowWidth INTEGER NOT NULL DEFAULT 0,
                    WindowHeight INTEGER NOT NULL DEFAULT 0
                );
CREATE TABLE "UserGoals" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UserGoals" PRIMARY KEY,
    "TargetType" TEXT NOT NULL,
    "TargetId" TEXT NULL,
    "DailyLimitMinutes" INTEGER NULL,
    "DailyTargetMinutes" INTEGER NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NULL
);
CREATE TABLE "WebSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WebSessions" PRIMARY KEY,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NULL,
    "Duration" INTEGER NOT NULL,
    "Url" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Domain" TEXT NOT NULL,
    "Favicon" TEXT NULL,
    "Browser" TEXT NOT NULL,
    "TabId" INTEGER NOT NULL,
    "ScrollDepth" INTEGER NOT NULL,
    "ClickCount" INTEGER NOT NULL,
    "HasFormInteraction" INTEGER NOT NULL,
    "Referrer" TEXT NULL,
    "Source" TEXT NULL,
    "IsActiveTab" INTEGER NOT NULL,
    "ActiveDuration" INTEGER NOT NULL
, IdleDuration INTEGER NOT NULL DEFAULT 0, CategoryId INTEGER, CategoryName TEXT, CategorySource TEXT, IsLegacy INTEGER NOT NULL DEFAULT 0);
CREATE TABLE "WebsiteCategories" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WebsiteCategories" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Color" TEXT NOT NULL,
    "Icon" TEXT NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NULL
);
CREATE TABLE "WebsiteDomainMappings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WebsiteDomainMappings" PRIMARY KEY AUTOINCREMENT,
    "DomainPattern" TEXT NOT NULL,
    "CategoryId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NULL,
    CONSTRAINT "FK_WebsiteDomainMappings_WebsiteCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "WebsiteCategories" ("Id") ON DELETE CASCADE
);
CREATE TABLE "WorkPatterns" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkPatterns" PRIMARY KEY,
    "Date" TEXT NOT NULL,
    "WorkStartTime" TEXT NULL,
    "WorkEndTime" TEXT NULL,
    "PeakHours" TEXT NULL,
    "FocusBlocks" TEXT NULL,
    "BreakCount" INTEGER NOT NULL,
    "TotalBreakMinutes" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "WorkflowSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkflowSessions" PRIMARY KEY,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NOT NULL,
    "WorkflowType" TEXT NOT NULL,
    "ContextSwitchCount" INTEGER NOT NULL,
    "FilesCreated" INTEGER NOT NULL,
    "FilesModified" INTEGER NOT NULL,
    "ClipboardOperations" INTEGER NOT NULL
);
CREATE INDEX IX_AiInsightSnapshots_CreatedAt ON AiInsightSnapshots (CreatedAt);
CREATE INDEX IX_AiInsightSnapshots_PeriodKey ON AiInsightSnapshots (PeriodKey);
CREATE INDEX IX_AppDailyStats_Date ON AppDailyStats (Date);
CREATE UNIQUE INDEX IX_AppDescriptions_ProcessName ON AppDescriptions (ProcessName);
CREATE INDEX "IX_AppSessions_ProcessName" ON "AppSessions" ("ProcessName");
CREATE INDEX "IX_AppSessions_StartTime" ON "AppSessions" ("StartTime");
CREATE INDEX "IX_AppSessions_StartTime_ProcessName" ON "AppSessions" ("StartTime", "ProcessName");
CREATE INDEX "IX_ArchiveRecords_CreatedAt" ON "ArchiveRecords" ("CreatedAt");
CREATE INDEX "IX_ArchiveRecords_DateRangeStart" ON "ArchiveRecords" ("DateRangeStart");
CREATE INDEX "IX_BackupRecords_CreatedAt" ON "BackupRecords" ("CreatedAt");
CREATE INDEX IX_BrowserBookmarks_IsDeleted ON BrowserBookmarks (IsDeleted);
CREATE INDEX IX_BrowserBookmarks_Type_Title ON BrowserBookmarks (Type, Title);
CREATE INDEX IX_BrowserBookmarks_Url ON BrowserBookmarks (Url);
CREATE INDEX IX_BrowserHistoryItems_Url ON BrowserHistoryItems (Url);
CREATE INDEX IX_BrowserHistoryItems_VisitTime ON BrowserHistoryItems (VisitTime);
CREATE UNIQUE INDEX "IX_DailyPatterns_Date" ON "DailyPatterns" ("Date");
CREATE UNIQUE INDEX IX_DailySummaries_Date ON DailySummaries (Date);
CREATE UNIQUE INDEX "IX_EfficiencyScores_Date" ON "EfficiencyScores" ("Date");
CREATE UNIQUE INDEX IX_HardwareSamples_Timestamp ON HardwareSamples (Timestamp);
CREATE INDEX "IX_InsightReports_ReportType" ON "InsightReports" ("ReportType");
CREATE INDEX "IX_InsightReports_StartDate" ON "InsightReports" ("StartDate");
-- 迁移（4.12.22）：旧版把每小时合成一行，进程名被最后一次写入覆盖，导致按应用的键鼠归属完全错。
-- 改成 (Date, Hour, ProcessName) 唯一，同一小时不同应用各占一行。
DROP INDEX IF EXISTS "IX_KeyboardSessions_Date_Hour";
DROP INDEX IF EXISTS "IX_MouseSessions_Date_Hour";
DROP INDEX IF EXISTS "IX_KeyboardSessions_Date_Hour_Process";
DROP INDEX IF EXISTS "IX_MouseSessions_Date_Hour_Process";
UPDATE "KeyboardSessions" SET "ProcessName" = '' WHERE "ProcessName" IS NULL;
UPDATE "MouseSessions" SET "ProcessName" = '' WHERE "ProcessName" IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_KeyboardSessions_Date_Hour_Process" ON "KeyboardSessions" ("Date", "Hour", "ProcessName");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_MouseSessions_Date_Hour_Process" ON "MouseSessions" ("Date", "Hour", "ProcessName");
CREATE UNIQUE INDEX IX_NetworkTrafficSamples_Timestamp ON NetworkTrafficSamples (Timestamp);
CREATE INDEX IX_ProcessNetworkUsages_Date ON ProcessNetworkUsages (Date);
CREATE UNIQUE INDEX "IX_ProgramCategories_Name" ON "ProgramCategories" ("Name");
CREATE INDEX "IX_ProgramCategories_SortOrder" ON "ProgramCategories" ("SortOrder");
CREATE INDEX "IX_ProgramCategoryMappings_CategoryId" ON "ProgramCategoryMappings" ("CategoryId");
CREATE INDEX "IX_ProgramCategoryMappings_ProcessName" ON "ProgramCategoryMappings" ("ProcessName");
CREATE UNIQUE INDEX "IX_ProgramCategoryMappings_ProcessName_CategoryId" ON "ProgramCategoryMappings" ("ProcessName", "CategoryId");
CREATE INDEX IX_StickyNotes_IsDeleted_IsArchived_UpdatedAt ON StickyNotes (IsDeleted, IsArchived, UpdatedAt);
CREATE INDEX IX_StickyNotes_IsPinned ON StickyNotes (IsPinned);
CREATE INDEX "IX_UserGoals_IsActive" ON "UserGoals" ("IsActive");
CREATE INDEX "IX_UserGoals_TargetType" ON "UserGoals" ("TargetType");
CREATE INDEX "IX_WebSessions_Domain" ON "WebSessions" ("Domain");
CREATE INDEX "IX_WebSessions_StartTime" ON "WebSessions" ("StartTime");
CREATE UNIQUE INDEX "IX_WebsiteCategories_Name" ON "WebsiteCategories" ("Name");
CREATE INDEX "IX_WebsiteCategories_SortOrder" ON "WebsiteCategories" ("SortOrder");
CREATE INDEX "IX_WebsiteDomainMappings_CategoryId" ON "WebsiteDomainMappings" ("CategoryId");
CREATE INDEX "IX_WebsiteDomainMappings_DomainPattern" ON "WebsiteDomainMappings" ("DomainPattern");
CREATE UNIQUE INDEX "IX_WebsiteDomainMappings_DomainPattern_CategoryId" ON "WebsiteDomainMappings" ("DomainPattern", "CategoryId");
CREATE UNIQUE INDEX "IX_WorkPatterns_Date" ON "WorkPatterns" ("Date");
CREATE INDEX "IX_WorkflowSessions_StartTime" ON "WorkflowSessions" ("StartTime");
