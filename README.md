AzTwitterSAR -- tweet analysis and notification
===============================================

This is an Azure Function that polls Twitter's API to obtain new tweets from
a given account. Then it scores the new tweetby the presence of specific words.
If this indicates that the tweet may be of interest, then a machine learning-based
filter function (see the sibling project
[TwitterSARai](https://github.com/lp-code/TwitterSARai))
is called. If that, too, indicates relevance, then the tweet is posted to a
slack channel through a webhook.


Prerequisites
-------------

* An Azure account and decent knowledge of deploying and running a serverless
  solution. ARM templates are included, i.e.
  the Azure infrastructure can be set up mostly automatically.
* Twitter API key + secret.
* The environment variable `AZTWITTERSAR_SLACKHOOK` must be set with a link
  to a web hook that should be used to post the notification. For local
  testing the definition must be in local.settings.json, on Azure it must
  be added in the deployment template parameters so that it can be written to
  the key vault.


Workflow when redeploying the code
----------------------------------
* Send a POST request to the terminatePostUri (obtained when starting the workflow).
* Send a POST request to the purgeHistoryDeleteUri.
* Stop the Azure function.
* Optionally: download the tweets from the storage account (can be done at any time).
* Optionally: delete or expunge the tables in the storage account.
* Run infrastructure and/or code deployment.
* Start the Azure function.
* Start the workflow by triggering the starter function from the Azure portal
  with the id of the last treated tweet as argument. Save the response with the
  durable function management webhooks.
