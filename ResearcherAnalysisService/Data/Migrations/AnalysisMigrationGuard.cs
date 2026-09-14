namespace ResearcherAnalysisService.Data.Migrations;

internal static class AnalysisMigrationGuard
{
    public static bool IsCompleteOrAbsent(string groupName, params bool[] objectsExist)
    {
        if (objectsExist.All(exists => exists))
            return true;
        if (objectsExist.All(exists => !exists))
            return false;

        throw new InvalidOperationException(
            $"The existing {groupName} schema is incomplete. Migration stopped without changing that group.");
    }
}
