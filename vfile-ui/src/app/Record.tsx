export default function Record({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <div className="btn btn-primary flex w-full flex-row items-center justify-start gap-4">
      {children}
    </div>
  );
}
