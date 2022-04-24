# Container Apps Acmebot

![Build](https://github.com/shibayan/containerapps-acmebot/workflows/Build/badge.svg)
[![Release](https://badgen.net/github/release/shibayan/containerapps-acmebot)](https://github.com/shibayan/containerapps-acmebot/releases/latest)
[![License](https://badgen.net/github/license/shibayan/containerapps-acmebot)](https://github.com/shibayan/containerapps-acmebot/blob/master/LICENSE)
[![Terraform Registry](https://badgen.net/badge/terraform/registry/5c4ee5)](https://registry.terraform.io/modules/shibayan/containerapps-acmebot/azurerm/latest)

This is an application that automates the issuance and renewal of ACME SSL/TLS certificates for Azure Container Apps.

- Support for multiple Container Apps and Container Apps Environment
- Easy to deploy and configure
- Highly reliable implementation
- Ease of Monitoring (Application Insights, Webhook)

You can add multiple certificates to a single Container Apps.

## Table Of Contents

- [Feature Support](#feature-support)
- [Requirements](#requirements)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Troubleshooting](#troubleshooting)
- [Thanks](#thanks)
- [License](#license)

## Feature Support

- Azure Container Apps (requires Azure DNS)
- Issuing certificates for Zone Apex Domains
- Issuing certificates with SANs (subject alternative names) (one certificate for multiple domains)
- Wildcard certificate (requires Azure DNS)
- Support for multiple Container Apps in a single application
- ACME-compliant Certification Authorities
  - [Let's Encrypt](https://letsencrypt.org/)
  - [Buypass Go SSL](https://www.buypass.com/ssl/resources/acme-free-ssl)
  - [ZeroSSL](https://zerossl.com/features/acme/) (Requires EAB Credentials)

## Requirements

- Azure Subscription
- Azure Container Apps and Azure DNS
- Email address (required to register with Let's Encrypt)

## Getting Started

### 1. Deploy Acmebot

| Azure (Public) | Azure China | Azure Government |
| :---: | :---: | :---: |
| <a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fshibayan%2Fcontainerapps-acmebot%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="https://aka.ms/deploytoazurebutton" /></a> | <a href="https://portal.azure.cn/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fshibayan%2Fcontainerapps-acmebot%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="https://aka.ms/deploytoazurebutton" /></a> | <a href="https://portal.azure.us/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fshibayan%2Fcontainerapps-acmebot%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="https://aka.ms/deploytoazurebutton" /></a> |

### 2. Add application settings

Update the following configuration settings of the Function App:

- `Acmebot:Webhook`
  - Webhook destination URL (optional, Slack and Microsoft Teams are recommended)

There are also additional settings that will be automatically created by Container Apps Acmebot:

- `Acmebot:Endpoint`
  - The ACME endpoint used to issue certificates
- `Acmebot:Contacts`
  - The email address (required) used in ACME account registration

### 3. Enable App Service Authentication

You must enable Authentication on the Function App that is deployed as part of this application.

In the Azure Portal, open the Function blade then select the `Authentication` menu and enable App Service authentication. Click on the `Add identity provider` button to display the screen for adding a new identity provider. If you select `Microsoft` as your Identity provider, the required settings will be automatically filled in for you. The default settings are fine.

![Add an Identity provider](https://user-images.githubusercontent.com/1356444/117532648-79e00300-b023-11eb-8cf1-92a11ffb115a.png)

Make sure that the App Service Authentication setting is set to `Require authentication`. The permissions can basically be left at the default settings.

![App Service Authentication settings](https://user-images.githubusercontent.com/1356444/117532660-8c5a3c80-b023-11eb-8573-df2e418d5c2f.png)

If you are using Sovereign Cloud, you may not be able to select Express. Enable authentication from the advanced settings with reference to the following document.

https://docs.microsoft.com/en-us/azure/app-service/configure-authentication-provider-aad#-configure-with-advanced-settings

Finally, you can save your previous settings to enable App Service authentication.

### 4. Add access control (IAM) to the target resource group

Open the `Access control (IAM)` of the target resource group and assign the roles `Contributor` to the deployed Container Apps and Azure DNS zones.

## Usage

### Issuing a new certificate

Access `https://YOUR-FUNCTIONS.azurewebsites.net/add-certificate` with a browser and authenticate with Azure Active Directory and the Web UI will be displayed. Select the target Container Apps Environment and DNS zone from that screen and run it, and after a few tens of seconds, the certificate will be issued.

![Add certificate](https://user-images.githubusercontent.com/1356444/164984750-58a73640-7455-4b9f-bc17-c3bdcb5efcf8.png)

If the `Access control (IAM)` setting is not correct, nothing will be shown in the drop-down list.

### Issuing a new certificate (REST API)

To automate the adding of certicates, you can use Acmebot's REST API.

```
POST /api/certificate

Content-Type: application/json
x-functions-key: asd+YourFunctionKeyHere+fgh==

{
  "managedEnvironmentName": "your-container-apps-env-name",
  "containerAppName": "your-container-apps-name",
  "dnsNames": [
    "example.com",
    "www.example.com"
  ]
}
```

### Renewing certificates

All existing ACME certificates are automatically renewed 30 days before their expiration.

The default check timing is 00:00 UTC. If you need to change the time zone, use `WEBSITE_TIME_ZONE` to set the time zone.

### Deploying a new version

The application is automatically updated so that you are always up to date with the latest version. If you explicitly need to deploy the latest version, restart the Azure Function.

## Thanks

- [ACMESharp Core](https://github.com/PKISharp/ACMESharpCore) by @ebekker
- [Durable Functions](https://github.com/Azure/azure-functions-durable-extension) by @cgillum and contributors
- [DnsClient.NET](https://github.com/MichaCo/DnsClient.NET) by @MichaCo

## License

This project is licensed under the [Apache License 2.0](https://github.com/shibayan/containerapps-acmebot/blob/master/LICENSE)
