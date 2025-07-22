const url = process.env.API_URL;

export type APIRequest = {
    vFilePath: string,
}

export type APIResponse = {
    result?: string,
    error?: {
        type: string,
        message: string
    },
}

export async function verifyVFile() {
    const request = { vFilePath: "C:\\dev\\twd-vfile2\\ThatWeebDorsey.vfile.db" } as APIRequest;
    console.log(request);
    const response = await fetch(`${url}/VFile/VerifyVFile`, {
        method: "POST",
        headers: { 
            'Content-Type': 'application/json', 
            'accept': 'text/plain',
        },
        body: JSON.stringify(request),
    });

    console.log(response.status);

    const json = await response.json();

    console.log(json);

    return json;
}
