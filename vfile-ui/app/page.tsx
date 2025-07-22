import { verifyVFile } from "./api";

export default async function Home() {
  const response = await verifyVFile();

  return (
    <div>
      Hello, World!
      {response.result}
    </div>
  );
}
