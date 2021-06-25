import {
  CheckboxVisibility,
  DefaultButton,
  DetailsList,
  DetailsListLayoutMode,
  FontIcon,
  IColumn,
  Link,
  Selection,
  SelectionMode,
  Text,
  TooltipHost,
} from "@fluentui/react";
import React, { useCallback, useEffect, useState } from "react";
import { Col, Container, Row } from "react-grid-system";
import { useTranslation } from "react-i18next";

import { useDataFileContext } from "context/DataFileContext";
import { StyledDropzone } from "components/StyledDropZone";
import { IElectronBridgeAction } from "global";
import { transformStringArrayToDetailListItems } from "common/Utility";
import { IpcRenderer } from "electron";
import toast from "react-hot-toast";
// styles
const iconStyles = {
  padding: 0,
  fontSize: "24px",
};

const detailsListStyles = {
  root: {
    cursor: "pointer",
    paddingTop: 20,
  },
};

// column definitions
const traceListColumns: IColumn[] = [
  {
    key: "column1",
    name: "Trace files",
    isIconOnly: true,
    fieldName: "file type",
    minWidth: 56,
    maxWidth: 56,
    onRender: () => (
      // todo: maybe detect different file types and load different ico
      <TooltipHost content="Trace file">
        <FontIcon aria-label="Compass" iconName="FileBug" style={iconStyles} />
      </TooltipHost>
    ),
  },
  {
    key: "column2",
    name: "",
    fieldName: "name",
    minWidth: 510,
  },
];

const Home: React.FC = () => {
  const { t } = useTranslation();
  const [files, setFiles] = useState<string[]>([]);
  const { setDataFile } = useDataFileContext();

  const onDrop = useCallback((acceptedFiles: File[]) => {
    acceptedFiles.forEach((file: File) => {
      //! https://github.com/react-dropzone/react-dropzone/issues/477
      // but it is working for some reason
      // eslint-disable-next-line @typescript-eslint/ban-ts-comment
      // @ts-ignore
      window.electronBridge.send("toMain", file.path);
      if (!window.electronBridge) {
      }
    });
  }, []);

  useEffect(() => {
    let removeListener: () => IpcRenderer;
    const getDirectoryListing = () => {
      fetch("/api/datadirectorylisting")
        .then(async (res: Response) => {
          if (res.ok) {
            const data = await res.json();
            setFiles(data);
          } else {
            const resTxt = await res.text();
            toast.error((t) => (
              <Container>
                <Row>
                  <Text block>{"There was an error "}</Text>
                  <Text variant={"xSmall"}>{resTxt}</Text>
                </Row>
                <Row justify="end" style={{ paddingTop: 7 }}>
                  <Col xs={4}>
                    <DefaultButton
                      onClick={() => {
                        getDirectoryListing();
                        toast.dismiss(t.id);
                      }}
                    >
                      Retry
                    </DefaultButton>
                  </Col>
                  <Col xs={4}>
                    <DefaultButton onClick={() => toast.dismiss(t.id)}>Dismiss</DefaultButton>
                  </Col>
                </Row>
              </Container>
            ));
          }
        })
        .catch((err: Error) => {
          console.log(err);
        });
    };
    getDirectoryListing();
    if (window.electronBridge) {
      removeListener = window.electronBridge.receive("fromMain", (action: IElectronBridgeAction) => {
        console.log(action);
        if (action === "reload") getDirectoryListing();
      });
    }
    //clean up listener
    return () => {
      if (removeListener) removeListener();
    };
  }, []);

  const selection = new Selection({
    onSelectionChanged: () => {
      if (selection.getSelectedCount() > 0) {
        // ?workaround for Fluent-UI, since it's always an array
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore
        const selectedFile = selection.getSelection()[0].value;
        setDataFile(selectedFile);
      }
    },
    selectionMode: SelectionMode.single,
  });
  return (
    <Container>
      <Row>
        <Col>
          <Text variant="xLarge" block>
            {t("home.title")}
          </Text>
          <Text variant="large" block>
            {t("home.intro1")}{" "}
            <Link target="_blank" href="https://github.com/microsoft/perfview/tree/main/src/PerfViewJS" underline>
              {t("home.githubtext")}
            </Link>
          </Text>
          <Text variant="large">
            {t("home.intro2")}
            <Link
              target="_blank"
              href="https://github.com/dotnet/diagnostics/blob/main/documentation/dotnet-trace-instructions.md"
              underline
            >
              {t("home.tracedocs")}
            </Link>
          </Text>
          <Text variant="large">
            {t("home.bugreporting")}{" "}
            <Link target="_blank" href="https://github.com/microsoft/perfview/issues" underline>
              {t("home.bugreportingtext")}
            </Link>
          </Text>
        </Col>
      </Row>
      <Row>
        <StyledDropzone onDrop={onDrop} />
      </Row>
      <Row>
        <Col>
          <DetailsList
            isHeaderVisible={false}
            checkboxVisibility={CheckboxVisibility.hidden}
            items={files ? transformStringArrayToDetailListItems(files) : []}
            styles={detailsListStyles}
            columns={traceListColumns}
            selection={selection}
            selectionMode={SelectionMode.single}
            layoutMode={DetailsListLayoutMode.justified}
            selectionPreservedOnEmptyClick={true}
          />
        </Col>
      </Row>
    </Container>
  );
};
export { Home };
