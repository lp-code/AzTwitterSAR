AzTwitterSAR -- tweet analysis and notification
===============================================

This is an Azure Function that receives individual tweets from a Logic App,
scores them by the presence of specific words, and - if a set threshold is
exceeded - sends a notification to a slack channel.


Prerequisites
-------------

* Currently no ARM templates are included, i.e. the Azure infrastructure has
  to be set up manually.
* The environment variable `AZTWITTERSAR_SLACKHOOK` must be set with a link
  to a web hook that should be used to post the notification. For local
  testing the definition must be in local.settings.json, on Azure it must
  be added in the application settings of the function.
