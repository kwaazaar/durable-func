# This script creates all the infrastructure using Azure CLI commands
# Prerequisites:
# - Azure CLI https://docs.microsoft.com/en-us/cli/azure/install-azure-cli
# - Azure Functions Core Tools https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=windows%2Ccsharp%2Cbash#install-the-azure-functions-core-tools
# More information https://markheath.net/post/deploying-azure-functions-with-azure-cli

# step 1 - log in 
az login

# step 2 - pick unique names
SUBSCRIPTION_NAME="Docati-Overig (d6b)"
RESOURCE_GROUP="ReportGenerator"
FUNCTION_APP_NAME="rtk1reportgenerator"
FUNCTION_APP_PLAN="$FUNCTION_APP_NAME-plan"
STORAGE_ACCOUNT_NAME=$FUNCTION_APP_NAME
APP_INSIGHTS_NAME=$FUNCTION_APP_NAME
LOCATION="westeurope"

# step 3 - ensure you are using the correct subscription
az account set -s "$SUBSCRIPTION_NAME"

# step 4 - create the resource group
az group create -n $RESOURCE_GROUP -l $LOCATION

# step 5 - create the storage account
az storage account create -n $STORAGE_ACCOUNT_NAME -l $LOCATION -g $RESOURCE_GROUP --sku Standard_LRS
STORAGE_ACCOUNT_KEY=$(az storage account keys list -n $STORAGE_ACCOUNT_NAME --query [0].value -o tsv)
STORAGE_ACCOUNT_CONNSTRING="DefaultEndpointsProtocol=https;AccountName=$STORAGE_ACCOUNT_NAME;AccountKey=$STORAGE_ACCOUNT_KEY;EndpointSuffix=core.windows.net"
# Create input folder
az storage container create --name input --account-name $STORAGE_ACCOUNT_NAME

# step 6 - create an Application Insights Instance
az resource create \
  -g $RESOURCE_GROUP -n $APP_INSIGHTS_NAME \
  --resource-type "Microsoft.Insights/components" \
  --properties "{\"Application_Type\":\"web\"}"

# step 7 - create the function app, connected to the storage account and app insights
# Warning: consumption plan does not support PDF-generation. Required sandbox API's are only available for dedicated plans (B1 and up).
az functionapp plan create \
  -g $RESOURCE_GROUP -n $FUNCTION_APP_PLAN --sku B1
az functionapp create \
  -n $FUNCTION_APP_NAME \
  --storage-account $STORAGE_ACCOUNT_NAME \
  --plan $FUNCTION_APP_PLAN \
  --app-insights $APP_INSIGHTS_NAME \
  --runtime dotnet \
  --functions-version 3 \
  --os-type Linux \
  -g $RESOURCE_GROUP

# step 8 - Configure settings
az functionapp config appsettings set -n $FUNCTION_APP_NAME -g $RESOURCE_GROUP \
  --settings "AzureWebJobsStorage=$STORAGE_ACCOUNT_CONNSTRING" "ReportStore=$STORAGE_ACCOUNT_CONNSTRING"

# Optional: set Docati-license (current one is valid until may 31st 2021)
az functionapp config appsettings set -n $FUNCTION_APP_NAME -g $RESOURCE_GROUP \
  --settings "DocatiLicense=<DocatiLicense xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Version>5</Version><Company>Docati Internal</Company><NativeFormatOnly>false</NativeFormatOnly><SingleLocation>true</SingleLocation><ImportTemplateSupported>true</ImportTemplateSupported><Trial>false</Trial><ValidFrom>2021-04-28T00:00:00</ValidFrom><ValidUntil>2021-05-31T00:00:00</ValidUntil><LicenseCreated>2021-04-28T00:00:00</LicenseCreated><Key>ngq/mm8efny3fj/eyhbwsdl7ylayqadz</Key></DocatiLicense>"

# step 9 - publish the application code
pushd ReportGenerator/
func azure functionapp publish $FUNCTION_APP_NAME
popd

# To remove all created resources, simply remove the resource group:
# az group delete -n $RESOURCE_GROUP
