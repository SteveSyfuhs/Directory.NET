.PHONY: build test frontend docker-build docker-up docker-down publish-win publish-linux clean

build:
	dotnet build src/src.sln

test:
	dotnet test src/src.sln

frontend:
	cd src/Directory.Web/ClientApp && npm ci && npm run build

docker-build:
	docker-compose build

docker-up:
	docker-compose up -d

docker-down:
	docker-compose down

publish-win:
	dotnet publish src/Directory.Server -c Release -o publish/server-win -r win-x64 --self-contained
	dotnet publish src/Directory.Web -c Release -o publish/web-win -r win-x64 --self-contained

publish-linux:
	dotnet publish src/Directory.Server -c Release -o publish/server-linux -r linux-x64 --self-contained
	dotnet publish src/Directory.Web -c Release -o publish/web-linux -r linux-x64 --self-contained

clean:
	dotnet clean src/src.sln
	rm -rf publish/
