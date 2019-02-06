
This bot comes from the Data and AI Bootcamp found at https://azure.github.io/LearnAI-Bootcamp/

## Architecture

![Architecture Diagram](./images/AI_Immersion_Arch.png)

## Deployment
At the bottom of PictureBot.cs, you need to add your Azure Search service name, key, and index name.

Near the bottom of Startup.cs, you need to add your LUIS App ID, Subscription key, and endpoint (e.g. https://westus.api.cognitive.microsoft.com/).


```shell
az login
az account list
az account set --subscription "-- your subscription id --"
```
### To connect your LUIS model to your bot
```shell
msbot connect luis --name "PictureBot" --appId "your LUIS app id" --version 0.1 --authoringKey "your authoring key" --subscriptionKey "your authoring key" --secret "-- your Bot File Secret --"
```

### If you need to update the LUIS model
```shell
msbot update luis --name "PictureBot" --appId "your LUIS app id" --version 0.1 --authoringKey "your authoring key" --subscriptionKey "your authoring key" --secret "-- your Bot File Secret --"
```

### Generate the NEW_LUIS_MODEL cs Class and add it in our project
```shell
msbot get pic --bot "BotConfiguration.bot" --secret "-- your Bot File Secret --" | luis export version --stdin > luispicture.json
luisgen luispicture.json -cs LUISPicture -o Models
```




