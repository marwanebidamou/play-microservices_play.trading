# play.trading
Play Economy Trading microservice.


## Build the docker image
```powershell
$version="1.0.1"
$env:GH_OWNER="play-microservice"
$env:GH_PAT="[GITHUB ACCESS TOKEN HERE]"
docker build --secret id=GH_OWNER --secret id=GH_PAT -t play.trading:$version .
```

## Run the docker image
### local version
```powershell
docker run -it --rm -p 5006:5006 --name trading -e MongoDbSettings__Host=mongo -e RabbitMQSettings__Host=rabbitmq --network playinfra_default play.trading:$version
```
### azure version
```powershell
$cosmosDbConnString="[CONN STRING HERE]"
$serviceBusConnString="[CONN STRING HERE]"
docker run -it --rm -p 5006:5006 --name trading -e MongoDbSettings__ConnectionString=$cosmosDbConnString -e ServiceBusSettings__ConnectionString=$serviceBusConnString -e ServiceSettings__MessageBroker="SERVICEBUS" play.trading:$version
```
## Publishing the Docker image
```powershell
$acrname="azacreconomy"
az acr login --name $acrname
docker tag play.trading:$version "$acrname.azurecr.io/play.trading:$version"
docker push "$acrname.azurecr.io/play.trading:$version"
```