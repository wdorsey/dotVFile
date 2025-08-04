"use client";

import React from "react";

export default function VFileSelector() {
  async function handleVFile(formData: FormData) {
    const vfile = formData.get("vfile") as string;
    console.log(vfile);
  }

  const [vfilePath, setVFilePath] = React.useState(
    "C:\\dev\\twd-vfile2\\ThatWeebDorsey.vfile.db",
  );

  return (
    <form action={handleVFile} className="flex w-full flex-row gap-2">
      <label className="input grow">
        <span className="label">vfile path:</span>
        <input
          type="text"
          name="vfile"
          placeholder="paste path here..."
          value={vfilePath}
          onChange={(e) => setVFilePath(e.target.value)}
        />
      </label>
      <button type="submit" className="btn">
        Submit
      </button>
    </form>
  );
}
