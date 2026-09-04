using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class FileLoggerTests
{
    [Fact]
    public void RecordsWhyADialogWasOrWasNotApproved()
    {
        var path = Path.Combine(Path.GetTempPath(), "cah-log-" + Guid.NewGuid().ToString("N") + ".log");
        var logger = new FileLogger(path);
        logger.Info("Detected JavaScript dialog host=analitikcms.test");
        logger.Info("Approved because host is allowlisted.");
        logger.Warn("Ignored github.com because it is not allowlisted.");

        var text = logger.ReadTail();

        Assert.Contains("Detected JavaScript dialog host=analitikcms.test", text);
        Assert.Contains("Approved because host is allowlisted.", text);
        Assert.Contains("Ignored github.com because it is not allowlisted.", text);
        Assert.Contains("[INFO]", text);
        Assert.Contains("[WARN]", text);
    }
}
