# Connect-AzAccount

New-AzResourceGroupDeployment `
    -ResourceGroupName rgTst `
    -TemplateFile .\aztwittersar_template.json `
    -TemplateParameterFile .\aztwittersar_parameters_secret.json
