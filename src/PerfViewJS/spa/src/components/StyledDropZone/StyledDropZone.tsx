import { getTheme, IStackTokens, Stack, Text } from "@fluentui/react";
import React, { useMemo } from "react";
import { DropEvent, FileRejection, useDropzone } from "react-dropzone";
import { Col, Container, Row } from "react-grid-system";
import { useTranslation } from "react-i18next";

const acceptedFileExt = ".etl,.btl,.netperf,.nettrace";
const theme = getTheme();
const baseStyle = {
  borderWidth: 2,
  borderRadius: 2,
  borderStyle: "dashed",
  borderColor: theme.semanticColors.bodyText,
  outline: "none",
  transition: "border .24s ease-in-out",
  cursor: "pointer",
};

const activeStyle = {
  borderColor: "#2196f3",
};

const acceptStyle = {
  borderColor: "#00e676",
};

const rejectStyle = {
  borderColor: "#ff1744",
};

const itemAlignmentsStackTokens: IStackTokens = {
  padding: 70,
};

export interface IStyledDropzone {
  onDrop: <T extends File>(acceptedFiles: T[], fileRejections: FileRejection[], event: DropEvent) => void;
}

const StyledDropzone = (props: IStyledDropzone) => {
  const { onDrop } = props;
  const { getRootProps, getInputProps, isDragActive, isDragAccept, isDragReject } = useDropzone({
    onDrop,
    accept: acceptedFileExt,
  });
  const { t } = useTranslation();
  const style = useMemo(
    () => ({
      ...baseStyle,
      ...(isDragActive ? activeStyle : {}),
      ...(isDragAccept ? acceptStyle : {}),
      ...(isDragReject ? rejectStyle : {}),
    }),
    [isDragActive, isDragReject, isDragAccept]
  );

  return (
    <Container style={{ width: "100%", paddingTop: "10px" }}>
      <Row align="center">
        <Col>
          <div {...getRootProps({ style })}>
            <input {...getInputProps()} />
            <Stack>
              <Stack.Item align="center" tokens={itemAlignmentsStackTokens}>
                <Text style={{ opacity: 0.6 }} variant="large">
                  {t("home.dropzonetext")}
                </Text>
              </Stack.Item>
            </Stack>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export { StyledDropzone };
