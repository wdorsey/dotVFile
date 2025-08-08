import { IoIosCheckmarkCircle } from "react-icons/io";
import { BiError } from "react-icons/bi";
import { FaFile, FaFolderClosed } from "react-icons/fa6";
import { IoArrowBack } from "react-icons/io5";

const defaultSize: number = 24;

export type IconProps = {
  className?: string;
  size?: number;
};

export function SuccessCheckmarkIcon(props: IconProps) {
  return (
    <IoIosCheckmarkCircle
      className={`fill-success ${props.className}`}
      size={props.size || defaultSize}
    />
  );
}

export function ErrorIcon(props: IconProps) {
  return (
    <BiError
      className={`fill-error ${props.className}`}
      size={props.size || defaultSize}
    />
  );
}

export function FolderIcon(props: IconProps) {
  return (
    <FaFolderClosed
      className={`fill-primary ${props.className}`}
      size={props.size || defaultSize}
    />
  );
}

export function FileIcon(props: IconProps) {
  return (
    <FaFile
      className={`fill-secondary ${props.className}`}
      size={props.size || defaultSize}
    />
  );
}

export function BackArrow(props: IconProps) {
  return (
    <IoArrowBack
      className={`${props.className}`}
      size={props.size || defaultSize}
    />
  );
}
