# ORCID API Kısa Raporu

## Kullanılan API

Proje, resmî ORCID Public API v3.0'ı kullanır:

```text
https://pub.orcid.org/v3.0
```

Temel çağrılar:

```text
GET /{ORCID}/record
GET /{ORCID}/works/{PUT-CODE-1},{PUT-CODE-2}
```

API yalnızca ORCID kaydında herkese açık olarak işaretlenmiş verileri döndürür.

## Yetkilendirme

Belgelenmiş kullanım için ücretsiz Public API kimlik bilgileri alınmalı ve
`/read-public` access token oluşturulmalıdır. Token her istekte yeniden alınmaz
veya harcanmaz. Aynı uzun ömürlü token okuma isteklerinde tekrar kullanılabilir:

```http
Authorization: Bearer ACCESS_TOKEN
```

## İstek Kotası

| API türü | Günlük kota | Hesaplama |
| --- | ---: | --- |
| Anonymous API | 25.000 okuma | IP adresi başına |
| Kayıtlı Public API | 100.000 okuma | Client ID başına |
| Member API | Günlük kota yok | Ücretli ORCID üyeliği |

Her HTTP `GET` çağrısı bir okuma sayılır. Tek istekte en fazla 100 eser toplu
alınabildiği için 100 eser tek kota okuması kullanabilir. Örneğin 250 eser için
bir profil ve üç toplu eser isteği yapılır; toplam dört okuma kullanılır.

## Önemli Sınırlamalar

- Public API h-index, i10-index ve atıf sayısı sağlamaz.
- Yalnızca herkese açık ORCID verileri alınabilir.
- Public API temel olarak ticari olmayan kullanım için ücretsizdir.
- Yoğun veya kurumsal kullanımda ORCID Member API gerekebilir.

## Resmî Kaynaklar

- [ORCID API v3.0 Guide](https://github.com/ORCID/orcid-model/blob/master/src/main/resources/record_3.0/README.md)
- [API kota ve hız sınırları](https://info.orcid.org/ufaqs/what-are-the-api-limits/)
- [Public API istemcisi kaydı](https://info.orcid.org/documentation/integration-guide/registering-a-public-api-client/)
- [ORCID kaydı okuma rehberi](https://info.orcid.org/documentation/api-tutorials/api-tutorial-read-data-on-a-record/)
- [Public API kullanım koşulları](https://info.orcid.org/public-client-terms-of-service/)
