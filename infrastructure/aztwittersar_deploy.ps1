# Connect-AzAccount

New-AzResourceGroupDeployment `
    -ResourceGroupName rg-rkh-twittersarvestpd-dev `
    -TemplateFile .\aztwittersar_template.json `
    -TemplateParameterFile .\aztwittersar_parameters.json
