HOST ?= http://localhost:5001
COLLECTOR_HOST ?= $(HOST)
ANALYSIS_HOST ?= http://localhost:5011

.PHONY: help run run-collector run-analysis build build-collector build-analysis clean health health-collector health-analysis collect

help:
	@echo "Kullanılabilir komutlar:"
	@echo "  make run                  Collector'ı başlatır (run-collector alias'ı)"
	@echo "  make run-collector        Collector'ı localhost:5001 üzerinde başlatır"
	@echo "  make run-analysis         Analysis Service'i localhost:5011 üzerinde başlatır"
	@echo "  make build                Solution'daki iki servisi derler"
	@echo "  make build-collector      Yalnız collector projesini derler"
	@echo "  make build-analysis       Yalnız Analysis Service projesini derler"
	@echo "  make clean                .NET build çıktılarını temizler"
	@echo "  make health               Collector sağlığını kontrol eder (health-collector alias'ı)"
	@echo "  make health-analysis      Analysis Service sağlığını kontrol eder"
	@echo "  make collect PERSONEL_ID=... ID=...  PersonelID ile akademik veri toplar"
	@echo "  make health HOST=...      Collector için farklı adres kullanır"
	@echo "  make health-analysis ANALYSIS_HOST=...  Analysis için farklı adres kullanır"

run: run-collector

run-collector:
	dotnet run --project AcademicCollectorDemo.csproj

run-analysis:
	dotnet run --project ResearcherAnalysisService/ResearcherAnalysisService.csproj --launch-profile http

build:
	dotnet build AcademicCollectorDemo.sln

build-collector:
	dotnet build AcademicCollectorDemo.csproj

build-analysis:
	dotnet build ResearcherAnalysisService/ResearcherAnalysisService.csproj

clean:
	dotnet clean AcademicCollectorDemo.sln
	@echo "Build çıktıları temizlendi. SQL Server veritabanına dokunulmadı."

health: health-collector

health-collector:
	curl --silent --show-error "$(COLLECTOR_HOST)/"
	@echo

health-analysis:
	curl --silent --show-error "$(ANALYSIS_HOST)/health"
	@echo

collect:
	@if [ -z "$(strip $(PERSONEL_ID))" ] || [ -z "$(strip $(ID))" ]; then \
		echo 'Kullanım: make collect PERSONEL_ID="00123-A" ID="0000-0001-8560-7482 A-1009-2008"'; \
		exit 1; \
	fi
	@response_file="$$(mktemp -t academic-collect.XXXXXX)"; \
	orcid=""; scholar_id=""; researcher_id=""; \
	for identifier in $(strip $(ID)); do \
		case "$$identifier" in \
			????-????-????-????) orcid="$$identifier" ;; \
			[A-Za-z]*-????-????) researcher_id="$$identifier" ;; \
			*) scholar_id="$$identifier" ;; \
		esac; \
	done; \
	request_body='{"PersonelID":"$(strip $(PERSONEL_ID))"'; separator=','; \
	if [ -n "$$orcid" ]; then \
		request_body="$${request_body}$${separator}\"ORCID\":\"$${orcid}\""; separator=','; \
	fi; \
	if [ -n "$$scholar_id" ]; then \
		request_body="$${request_body}$${separator}\"ScholarID\":\"$${scholar_id}\""; separator=','; \
	fi; \
	if [ -n "$$researcher_id" ]; then \
		request_body="$${request_body}$${separator}\"ResearcherID\":\"$${researcher_id}\""; \
	fi; \
	request_body="$${request_body}}"; \
	start_time="$$(date +%s)"; \
	echo "Toplama isteği gönderildi. Akademik kaynaklar bekleniyor..."; \
	curl --silent --show-error \
		--fail-with-body \
		--request POST \
		--header "Content-Type: application/json" \
		--data "$$request_body" \
		"$(COLLECTOR_HOST)/Services/AcademicPerformance/V1/Collect" \
		> "$$response_file" & \
	request_pid="$$!"; \
	while kill -0 "$$request_pid" 2>/dev/null; do \
		current_time="$$(date +%s)"; \
		elapsed_seconds="$$((current_time - start_time))"; \
		printf '\rİşlem: %s saniye' "$$elapsed_seconds"; \
		sleep 1; \
	done; \
	if wait "$$request_pid"; then \
		printf '\rİşlem tamamlandı.                                  \n'; \
		cat "$$response_file"; \
		echo; \
		status=0; \
	else \
		status="$$?"; \
		printf '\rİstek hata ile tamamlandı.                          \n'; \
		cat "$$response_file"; \
		echo; \
	fi; \
	rm -f -- "$$response_file"; \
	exit "$$status"
