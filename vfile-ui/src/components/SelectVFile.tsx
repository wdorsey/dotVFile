import React from "react";
import { IoIosCheckmarkCircle } from "react-icons/io";
import { BiError } from "react-icons/bi";

export default function SelectVFile({
  initialPath,
  vfilePathVerified,
  onVFilePathSubmitted,
}: {
  initialPath: string | undefined;
  vfilePathVerified: boolean;
  onVFilePathSubmitted: ({ vfilePath }: { vfilePath: string }) => void;
}) {
  const [vfilePathValue, setVFilePathValue] = React.useState(initialPath);

  return (
    <>
      <form
        action={(formData: FormData) =>
          onVFilePathSubmitted({ vfilePath: formData.get("vfile") as string })
        }
        className="flex w-full flex-row gap-2"
      >
        <label className="input grow">
          <span className="label">vfile path:</span>
          {vfilePathVerified ? (
            <IoIosCheckmarkCircle size={24} className="fill-success" />
          ) : (
            <BiError size={24} className="fill-error" />
          )}
          <input
            type="text"
            name="vfile"
            placeholder="paste path here..."
            value={vfilePathValue}
            onChange={(e) => setVFilePathValue(e.target.value)}
          />
        </label>
        <button type="submit" className="btn">
          Submit
        </button>
      </form>
    </>
  );
}
