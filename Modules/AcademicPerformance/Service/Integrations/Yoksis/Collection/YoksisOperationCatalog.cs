namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

internal static class YoksisOperationCatalog
{
    public static readonly IReadOnlyList<YoksisOperationDefinition> All =
    [
        new(
            "Dersler",
            "getirDersListesi",
            "getirDersListesiRequest"),
        new(
            "Üniversite dışı deneyimler",
            "getirUnvDisiDeneyimListesi",
            "getirUnvDisiDeneyimListesiRequest"),
        new(
            "Tez danışmanlıkları",
            "getirTezDanismanListesi",
            "getirTezDanismanListesiRequest"),
        new(
            "Hakemlikler",
            "getHakemlikBilgisiV1",
            "getHakemlikBilgisiV1Request"),
        new(
            "Bildiriler",
            "getBildiriBilgisiV1",
            "getBildiriBilgisiV1Request",
            "Bildiri ayrıntıları",
            "getBildiriBilgisiDetayV1",
            "getBildiriBilgisiDetayV1Request",
            "YAYIN_ID"),
        new(
            "Tasarımlar",
            "getTasarimBilgisiV1",
            "getTasarimBilgisiV1Request"),
        new(
            "Personel ve araştırmacı kimlikleri",
            "getPersonelLinkV1",
            "getPersonelLinkV1Request"),
        new(
            "Ödüller",
            "getOdulListesiV1",
            "getOdulListesiV1Request"),
        new(
            "Araştırma ve sertifikalar",
            "getArastirmaSertifkaBilgisiV1",
            "getArastirmaSertifkaBilgisiV1Request"),
        new(
            "Makaleler",
            "getMakaleBilgisiV1",
            "getMakaleBilgisiV1Request",
            "Makale ayrıntıları",
            "getMakaleBilgisiDetayV1",
            "getMakaleBilgisiDetayV1Request",
            "YAYIN_ID"),
        new(
            "Projeler",
            "getirProjeListesi",
            "getirProjeListesiRequest",
            "Proje ayrıntıları",
            "getirProjeListesiDetay",
            "getirProjeListesiDetayRequest",
            "PROJE_ID"),
        new(
            "Akademik görevler",
            "getirAkademikGorevListesi",
            "getirAkademikGorevListesiRequest"),
        new(
            "Kitaplar",
            "getKitapBilgisiV1",
            "getKitapBilgisiV1Request",
            "Kitap ayrıntıları",
            "getKitapBilgisiDetayV1",
            "getKitapBilgisiDetayV1Request",
            "YAYIN_ID"),
        new(
            "İdari görevler",
            "getirIdariGorevListesi",
            "getirIdariGorevListesiRequest"),
        new(
            "Temel alanlar",
            "getTemelAlanBilgisiV1",
            "getTemelAlanBilgisiV1Request"),
        new(
            "Öğrenim bilgileri",
            "getirOgrenimBilgisiListesi",
            "getirOgrenimBilgisiListesiRequest"),
        new(
            "Yabancı diller",
            "getirYabanciDilListesi",
            "getirYabanciDilListesiRequest"),
        new(
            "Patentler",
            "getPatentBilgisiV1",
            "getPatentBilgisiV1Request",
            "Patent ayrıntıları",
            "getPatentBilgisiDetayV1",
            "getPatentBilgisiDetayV1Request",
            "PATENT_ID"),
        new(
            "Üyelikler",
            "getirUyelikListesi",
            "getirUyelikListesiRequest"),
        new(
            "Editörlükler",
            "getEditorlukBilgisiV1",
            "getEditorlukBilgisiV1Request"),
        new(
            "Sanatsal faaliyetler",
            "getSanatsalFaalV1",
            "getSanatsalFaalV1Request")
    ];
}
