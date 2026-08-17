# Academic Collector Demo

Bu küçük uygulama bir ORCID numarasını kullanarak OpenAlex'ten akademisyen
bilgisini ve son 10 yayınını getirir. Henüz veritabanına kayıt yapmaz.

Test için Albert-László Barabási'nin kamuya açık ORCID numarası kullanılır:

```text
0000-0002-4028-3522
```

## Çalıştırma

.NET 10 SDK ile proje klasöründe:

```shell
dotnet run
```

Başka bir ORCID denemek için:

```shell
dotnet run -- 0000-0003-2812-9917
```

Program akademisyenin adını, toplam yayın sayısını ve en yeni 10 yayını
terminalde gösterir.
