// Cosmos DB post-trigger that updates run object after updating thw file object
function updateMetadata() {
    var context = getContext();
    var container = context.getCollection();
    var response = context.getResponse();

    // item that was created
    var fileItem = response.getBody();

    var accept = container.readDocument(`${container.getAltLink()}/docs/$`, updateMetadataCallback);
    if (!accept) throw "readDocument declined, abort";

    function updateMetadataCallback(err, runItem) {
        if (err) throw new Error("Error" + err.message);

        if (!runItem) throw 'Unable to read run object';

        var changed = false;
        for (const source in fileItem.variables) {
            if (runItem.variables && runItem.variables[source]) {
                const v_set = new Set(runItem.variables[source]);
                const s_set = v_set.size;
                for (const v of fileItem.variables[source]) v_set.add(v);
                if (v_set.size > s_set) {
                    runItem.variables[source] = [...v_set];
                    changed = true;
                }
            } else {
                if (runItem.variables) {
                    runItem.variables[source] = fileItem.variables[source];
                } else {
                    runItem.variables = fileItem.variables;
                }
                changed = true;
            }
        }
        if (changed) {
            var accept = container.replaceDocument(runItem._self,
                runItem, function (err) {
                    if (err) throw "Unable to update run object, abort";
                });
            if (!accept) throw "Run object update declined, abort";
        }
        return;
    }
}