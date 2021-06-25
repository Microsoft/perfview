import base64url from "base64url";
import React from "react";
import { useLocalStorage } from "../hooks/useLocalStorage";

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore
const DataFileContext = React.createContext<DataFileContextType>();
export type DataFileContextType = {
  dataFile: string;
  setDataFile: (dataFile: string) => void;
  dataFileName: string;
};

interface IDataFileContextProviderProp {
  children: JSX.Element;
}

export const DataFileContextProvider = (props: IDataFileContextProviderProp) => {
  const { children } = props;
  const [dataFile, _setDataFile] = useLocalStorage<string>("dataFile", "");

  const dataFileName = base64url.decode(dataFile).replace(/\*/g, "");

  const setDataFile = (_dataFile: string) => _setDataFile(base64url.encode(`${_dataFile}**`));
  const value = { dataFile, setDataFile, dataFileName };
  return <DataFileContext.Provider value={value}>{children}</DataFileContext.Provider>;
};

export const useDataFileContext = () => {
  const context = React.useContext(DataFileContext);
  if (context === undefined) throw new Error("useDataFile context must be used within DataFileContextProvider");
  return context;
};
