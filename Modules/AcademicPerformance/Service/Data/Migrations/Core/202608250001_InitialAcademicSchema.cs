using FluentMigrator;
using System.Data;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations;

[Migration(202608250001, "Create the initial academic performance schema")]
public sealed class InitialAcademicSchema : Migration
{
    public override void Up()
    {
        CreateResearchers();
        CreateOrcidProfiles();
        CreateOrcidWorks();
        CreateWebOfScienceProfiles();
        CreateWebOfScienceWorks();
        CreateWebOfSciencePeerReviews();
        CreateYoksisRecords();
        CreateAcademicWorks();
        CreatePublicationSummaries();
        CreatePublicationDisplayApprovals();
        CreateIndexes();
    }

    public override void Down()
    {
        Delete.Table("PublicationDisplayApprovals");
        Delete.Table("PublicationSummaries");
        Delete.Table("AcademicWorks");
        Delete.Table("YoksisRecords");
        Delete.Table("WebOfSciencePeerReviews");
        Delete.Table("WebOfScienceWorks");
        Delete.Table("WebOfScienceProfiles");
        Delete.Table("OrcidWorks");
        Delete.Table("OrcidProfiles");
        Delete.Table("Researchers");
    }

    private void CreateResearchers()
    {
        Create.Table("Researchers")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("UniversityPersonnelId").AsString(int.MaxValue).Nullable()
            .WithColumn("FirstName").AsString(int.MaxValue).Nullable()
            .WithColumn("LastName").AsString(int.MaxValue).Nullable()
            .WithColumn("AcademicTitle").AsString(int.MaxValue).Nullable()
            .WithColumn("Department").AsString(int.MaxValue).Nullable()
            .WithColumn("Orcid").AsString(19).Nullable()
            .WithColumn("WebOfScienceResearcherId").AsString(20).Nullable()
            .WithColumn("YoksisResearcherId").AsString(50).Nullable()
            .WithColumn("LastUpdatedAt").AsDateTime().Nullable();
    }

    private void CreateOrcidProfiles()
    {
        Create.Table("OrcidProfiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_OrcidProfiles_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("DisplayName").AsString(500).Nullable()
            .WithColumn("GivenNames").AsString(250).Nullable()
            .WithColumn("FamilyName").AsString(250).Nullable()
            .WithColumn("CreditName").AsString(500).Nullable()
            .WithColumn("Biography").AsString(int.MaxValue).Nullable()
            .WithColumn("CountryCodes").AsString(250).Nullable()
            .WithColumn("Keywords").AsString(4000).Nullable()
            .WithColumn("CurrentOrganization").AsString(1000).Nullable()
            .WithColumn("CurrentDepartment").AsString(1000).Nullable()
            .WithColumn("CurrentRoleTitle").AsString(500).Nullable()
            .WithColumn("WorksCount").AsInt32().NotNullable()
            .WithColumn("EmploymentsCount").AsInt32().NotNullable()
            .WithColumn("EducationsCount").AsInt32().NotNullable()
            .WithColumn("FundingsCount").AsInt32().NotNullable()
            .WithColumn("PeerReviewsCount").AsInt32().NotNullable()
            .WithColumn("RecordLastModifiedAt").AsDateTime().Nullable()
            .WithColumn("LastUpdatedAt").AsDateTime().NotNullable()
            .WithColumn("ResearcherUrlsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("ExternalIdentifiersJson").AsString(int.MaxValue).Nullable()
            .WithColumn("EmploymentsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("EducationsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("ActivitiesJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
    }

    private void CreateOrcidWorks()
    {
        Create.Table("OrcidWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("OrcidProfileId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_OrcidWorks_OrcidProfiles_OrcidProfileId",
                    "OrcidProfiles",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("PutCode").AsInt64().NotNullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("Subtitle").AsString(2000).Nullable()
            .WithColumn("TranslatedTitle").AsString(2000).Nullable()
            .WithColumn("WorkType").AsString(100).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationDate").AsDateTime().Nullable()
            .WithColumn("JournalTitle").AsString(2000).Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("Url").AsString(2000).Nullable()
            .WithColumn("Authors").AsString(4000).Nullable()
            .WithColumn("LanguageCode").AsString(20).Nullable()
            .WithColumn("CountryCode").AsString(20).Nullable()
            .WithColumn("ShortDescription").AsString(int.MaxValue).Nullable()
            .WithColumn("Citation").AsString(int.MaxValue).Nullable()
            .WithColumn("SourceName").AsString(500).Nullable()
            .WithColumn("Visibility").AsString(50).Nullable()
            .WithColumn("RecordLastModifiedAt").AsDateTime().Nullable()
            .WithColumn("Category").AsString(50).NotNullable()
            .WithColumn("CategorySource").AsString(50).NotNullable()
            .WithColumn("ExternalIdentifiersJson").AsString(int.MaxValue).Nullable()
            .WithColumn("ContributorsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
    }

    private void CreateWebOfScienceProfiles()
    {
        Create.Table("WebOfScienceProfiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_WebOfScienceProfiles_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("DisplayName").AsString(500).Nullable()
            .WithColumn("FirstName").AsString(250).Nullable()
            .WithColumn("LastName").AsString(250).Nullable()
            .WithColumn("Orcid").AsString(19).Nullable()
            .WithColumn("IsClaimed").AsBoolean().NotNullable()
            .WithColumn("PrimaryOrganization").AsString(1000).Nullable()
            .WithColumn("PrimaryAddress").AsString(2000).Nullable()
            .WithColumn("PrimaryCountry").AsString(250).Nullable()
            .WithColumn("Departments").AsString(2000).Nullable()
            .WithColumn("HIndex").AsInt32().Nullable()
            .WithColumn("DocumentsCount").AsInt32().NotNullable()
            .WithColumn("TotalCitingPublications").AsInt32().Nullable()
            .WithColumn("TotalCitingWithoutSelf").AsInt32().Nullable()
            .WithColumn("TotalTimesCited").AsInt32().Nullable()
            .WithColumn("TotalTimesCitedWithoutSelf").AsInt32().Nullable()
            .WithColumn("PeerReviewsCount").AsInt32().NotNullable()
            .WithColumn("LastUpdatedAt").AsDateTime().NotNullable()
            .WithColumn("AlternativeNamesJson").AsString(int.MaxValue).Nullable()
            .WithColumn("AffiliationsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("AuthorPositionsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("SubjectCategoriesJson").AsString(int.MaxValue).Nullable()
            .WithColumn("AwardsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable()
            .WithColumn("DocumentPagesJson").AsString(int.MaxValue).Nullable()
            .WithColumn("PeerReviewPagesJson").AsString(int.MaxValue).Nullable();
    }

    private void CreateWebOfScienceWorks()
    {
        Create.Table("WebOfScienceWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("WebOfScienceProfileId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_WebOfScienceWorks_WebOfScienceProfiles_WebOfScienceProfileId",
                    "WebOfScienceProfiles",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("Uid").AsString(100).Nullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("WorkTypes").AsString(500).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationDate").AsDateTime().Nullable()
            .WithColumn("SourceTitle").AsString(2000).Nullable()
            .WithColumn("Volume").AsString(100).Nullable()
            .WithColumn("Issue").AsString(100).Nullable()
            .WithColumn("Collection").AsString(100).Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("TimesCited").AsInt32().Nullable()
            .WithColumn("Category").AsString(50).NotNullable()
            .WithColumn("CategorySource").AsString(50).NotNullable()
            .WithColumn("CitationsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
    }

    private void CreateWebOfSciencePeerReviews()
    {
        Create.Table("WebOfSciencePeerReviews")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("WebOfScienceProfileId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_WebOfSciencePeerReviews_WebOfScienceProfiles_WebOfScienceProfileId",
                    "WebOfScienceProfiles",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("Journal").AsString(2000).Nullable()
            .WithColumn("Publisher").AsString(2000).Nullable()
            .WithColumn("DateOfReview").AsString(100).Nullable()
            .WithColumn("Verified").AsString(20).Nullable()
            .WithColumn("ArticleTitle").AsString(2000).Nullable()
            .WithColumn("ArticleDoi").AsString(500).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
    }

    private void CreateYoksisRecords()
    {
        Create.Table("YoksisRecords")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_YoksisRecords_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("CategoryName").AsString(250).NotNullable()
            .WithColumn("OperationName").AsString(250).NotNullable()
            .WithColumn("RecordIndex").AsInt32().NotNullable()
            .WithColumn("ExternalRecordId").AsString(500).Nullable()
            .WithColumn("RecordJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("CollectedAt").AsDateTime().NotNullable();
    }

    private void CreateAcademicWorks()
    {
        Create.Table("AcademicWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_AcademicWorks_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("Provider").AsString(50).NotNullable()
            .WithColumn("ProviderWorkId").AsString(500).Nullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationDate").AsDateTime().Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("RawType").AsString(100).Nullable()
            .WithColumn("Category").AsString(50).NotNullable()
            .WithColumn("CategorySource").AsString(50).NotNullable()
            .WithColumn("CitedByCount").AsInt32().Nullable()
            .WithColumn("ReferencedWorksCount").AsInt32().Nullable()
            .WithColumn("Authors").AsString(4000).Nullable()
            .WithColumn("Institutions").AsString(4000).Nullable()
            .WithColumn("Abstract").AsString(int.MaxValue).Nullable()
            .WithColumn("Keywords").AsString(4000).Nullable()
            .WithColumn("Topics").AsString(4000).Nullable()
            .WithColumn("Language").AsString(20).Nullable()
            .WithColumn("Publication").AsString(2000).Nullable()
            .WithColumn("Volume").AsString(100).Nullable()
            .WithColumn("Issue").AsString(100).Nullable()
            .WithColumn("FirstPage").AsString(100).Nullable()
            .WithColumn("LastPage").AsString(100).Nullable()
            .WithColumn("Link").AsString(2000).Nullable()
            .WithColumn("SourceId").AsString(500).Nullable()
            .WithColumn("SourceName").AsString(2000).Nullable()
            .WithColumn("SourceType").AsString(100).Nullable()
            .WithColumn("SourceUrl").AsString(2000).Nullable()
            .WithColumn("IsOpenAccess").AsBoolean().Nullable()
            .WithColumn("OpenAccessStatus").AsString(50).Nullable()
            .WithColumn("OpenAccessUrl").AsString(2000).Nullable()
            .WithColumn("HasFullText").AsBoolean().Nullable()
            .WithColumn("FullTextUrl").AsString(2000).Nullable()
            .WithColumn("License").AsString(100).Nullable()
            .WithColumn("Version").AsString(100).Nullable()
            .WithColumn("IsRetracted").AsBoolean().Nullable()
            .WithColumn("ProviderPayload").AsString(int.MaxValue).Nullable()
            .WithColumn("SyncedAt").AsDateTime().NotNullable();
    }

    private void CreatePublicationSummaries()
    {
        Create.Table("PublicationSummaries")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_PublicationSummaries_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("Fingerprint").AsString(64).NotNullable()
            .WithColumn("Title").AsString(2000).NotNullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("Category").AsString(50).NotNullable()
            .WithColumn("Authors").AsString(4000).Nullable()
            .WithColumn("Publication").AsString(2000).Nullable()
            .WithColumn("PublicationUrl").AsString(2000).Nullable()
            .WithColumn("Sources").AsString(200).NotNullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable();
    }

    private void CreatePublicationDisplayApprovals()
    {
        Create.Table("PublicationDisplayApprovals")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_PublicationDisplayApprovals_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
            .WithColumn("PublicationSummaryId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_PublicationDisplayApprovals_PublicationSummaries_PublicationSummaryId",
                    "PublicationSummaries",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("ApprovedAt").AsDateTime().NotNullable();
    }

    private void CreateIndexes()
    {
        CreateResearcherIdentifierIndex("IX_Researchers_Orcid", "Orcid");
        CreateResearcherIdentifierIndex(
            "IX_Researchers_WebOfScienceResearcherId",
            "WebOfScienceResearcherId");
        CreateResearcherIdentifierIndex(
            "IX_Researchers_YoksisResearcherId",
            "YoksisResearcherId");

        CreateUniqueIndex(
            "IX_OrcidProfiles_ResearcherId",
            "OrcidProfiles",
            "ResearcherId");
        CreateUniqueIndex(
            "IX_OrcidWorks_OrcidProfileId_PutCode",
            "OrcidWorks",
            "OrcidProfileId",
            "PutCode");
        CreateUniqueIndex(
            "IX_WebOfScienceProfiles_ResearcherId",
            "WebOfScienceProfiles",
            "ResearcherId");
        CreateUniqueIndex(
            "IX_WebOfScienceWorks_WebOfScienceProfileId_Uid",
            "WebOfScienceWorks",
            "WebOfScienceProfileId",
            "Uid");
        CreateIndex(
            "IX_WebOfSciencePeerReviews_WebOfScienceProfileId",
            "WebOfSciencePeerReviews",
            "WebOfScienceProfileId");
        CreateIndex(
            "IX_YoksisRecords_ResearcherId",
            "YoksisRecords",
            "ResearcherId");
        CreateIndex(
            "IX_YoksisRecords_ResearcherId_OperationName",
            "YoksisRecords",
            "ResearcherId",
            "OperationName");
        CreateIndex(
            "IX_AcademicWorks_ResearcherId",
            "AcademicWorks",
            "ResearcherId");
        CreateIndex(
            "IX_AcademicWorks_ResearcherId_Provider",
            "AcademicWorks",
            "ResearcherId",
            "Provider");
        CreateIndex(
            "IX_PublicationSummaries_ResearcherId",
            "PublicationSummaries",
            "ResearcherId");
        CreateUniqueIndex(
            "IX_PublicationSummaries_ResearcherId_Fingerprint",
            "PublicationSummaries",
            "ResearcherId",
            "Fingerprint");
        CreateIndex(
            "IX_PublicationDisplayApprovals_ResearcherId",
            "PublicationDisplayApprovals",
            "ResearcherId");
        CreateUniqueIndex(
            "IX_PublicationDisplayApprovals_PublicationSummaryId",
            "PublicationDisplayApprovals",
            "PublicationSummaryId");
    }

    private void CreateResearcherIdentifierIndex(
        string indexName,
        string columnName)
    {
        Execute.Sql(
            $"CREATE UNIQUE INDEX [{indexName}] ON [Researchers] " +
            $"([{columnName}]) WHERE [{columnName}] IS NOT NULL;");
    }

    private void CreateIndex(
        string indexName,
        string tableName,
        params string[] columnNames)
    {
        if (columnNames.Length == 1)
        {
            Create.Index(indexName)
                .OnTable(tableName)
                .OnColumn(columnNames[0])
                .Ascending();
            return;
        }

        Create.Index(indexName)
            .OnTable(tableName)
            .OnColumn(columnNames[0])
            .Ascending()
            .OnColumn(columnNames[1])
            .Ascending();
    }

    private void CreateUniqueIndex(
        string indexName,
        string tableName,
        params string[] columnNames)
    {
        if (columnNames.Length == 1)
        {
            Create.Index(indexName)
                .OnTable(tableName)
                .OnColumn(columnNames[0])
                .Ascending()
                .WithOptions()
                .Unique();
            return;
        }

        Create.Index(indexName)
            .OnTable(tableName)
            .OnColumn(columnNames[0])
            .Ascending()
            .OnColumn(columnNames[1])
            .Ascending()
            .WithOptions()
            .Unique();
    }
}
